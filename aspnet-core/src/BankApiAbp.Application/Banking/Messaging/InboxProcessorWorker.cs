using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace BankApiAbp.Banking.Messaging;

public class InboxProcessorWorker : BackgroundService, ITransientDependency
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InboxProcessorWorker> _logger;

    public InboxProcessorWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<InboxProcessorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InboxProcessorWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InboxProcessorWorker batch failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("InboxProcessorWorker stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var batchActivity = InboxTracing.ActivitySource.StartActivity("inbox.process.batch");

        using var scope = _scopeFactory.CreateScope();

        var distributedLock = scope.ServiceProvider.GetRequiredService<IAbpDistributedLock>();

        await using var lockHandle = await distributedLock.TryAcquireAsync(
            "inbox:processor",
            TimeSpan.FromSeconds(20),
            cancellationToken: cancellationToken);

        if (lockHandle == null)
        {
            batchActivity?.SetTag("inbox.lock.acquired", false);
            _logger.LogDebug("InboxProcessorWorker skipped because distributed lock could not be acquired.");
            return;
        }

        batchActivity?.SetTag("inbox.lock.acquired", true);

        var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var inboxRepository = scope.ServiceProvider.GetRequiredService<IRepository<InboxMessage, Guid>>();

        Guid[] messageIds;

        using (var uow = uowManager.Begin(requiresNew: true, isTransactional: false))
        {
            var now = DateTime.UtcNow;

            var candidates = await inboxRepository.GetListAsync(x =>
                x.Status == InboxMessageStatus.Pending ||
                (x.Status == InboxMessageStatus.Retrying &&
                 x.NextRetryTime != null &&
                 x.NextRetryTime <= now));

            messageIds = candidates
                .OrderBy(x => x.CreationTime)
                .Take(20)
                .Select(x => x.Id)
                .ToArray();

            await uow.CompleteAsync();
        }

        batchActivity?.SetTag("inbox.batch.size", messageIds.Length);

        if (messageIds.Length == 0)
        {
            _logger.LogDebug("InboxProcessorWorker found no pending messages.");
            return;
        }

        InboxMetrics.MessagesPicked.Add(messageIds.Length);

        _logger.LogInformation("InboxProcessorWorker picked {Count} inbox message(s).", messageIds.Length);

        foreach (var messageId in messageIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessSingleAsync(messageId, cancellationToken);
        }
    }

    private async Task ProcessSingleAsync(Guid inboxMessageId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var inboxRepository = scope.ServiceProvider.GetRequiredService<IRepository<InboxMessage, Guid>>();
        var inboxManager = scope.ServiceProvider.GetRequiredService<IInboxManager>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IInboxEventDispatcher>();

        Guid eventId;
        string eventName;
        string consumerName;
        string? payloadHash;
        string? payloadJson;
        int maxRetryCount;
        int retryCount;
        string status;

        using (var readUow = uowManager.Begin(requiresNew: true, isTransactional: false))
        {
            var msg = await inboxRepository.GetAsync(inboxMessageId);

            eventId = msg.EventId;
            eventName = msg.EventName;
            consumerName = msg.ConsumerName;
            payloadHash = msg.PayloadHash;
            payloadJson = msg.PayloadJson;
            maxRetryCount = msg.MaxRetryCount;
            retryCount = msg.RetryCount;
            status = msg.Status.ToString();

            await readUow.CompleteAsync();
        }

        using var activity = InboxTracing.ActivitySource.StartActivity("inbox.process.message");

        activity?.SetTag("inbox.message.id", inboxMessageId);
        activity?.SetTag("inbox.event.id", eventId);
        activity?.SetTag("inbox.event.name", eventName);
        activity?.SetTag("inbox.consumer.name", consumerName);
        activity?.SetTag("inbox.retry.count", retryCount);
        activity?.SetTag("inbox.max_retry.count", maxRetryCount);
        activity?.SetTag("inbox.status", status);

        var decision = await inboxManager.TryBeginProcessingAsync(
            eventId,
            eventName,
            consumerName,
            payloadHash,
            payloadJson,
            maxRetryCount);

        activity?.SetTag("inbox.should_process", decision.ShouldProcess);

        if (!decision.ShouldProcess)
        {
            activity?.SetTag("inbox.result", "skipped");

            _logger.LogDebug(
                "Inbox message skipped. InboxMessageId={InboxMessageId}",
                inboxMessageId);

            return;
        }

        var actualInboxMessageId = decision.InboxMessageId ?? inboxMessageId;
        activity?.SetTag("inbox.actual_message.id", actualInboxMessageId);

        var sw = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await dispatcher.DispatchAsync(actualInboxMessageId);
            await inboxManager.MarkProcessedAsync(actualInboxMessageId);

            sw.Stop();
            InboxMetrics.MessagesProcessed.Add(1);
            InboxMetrics.ProcessingDurationMs.Record(sw.Elapsed.TotalMilliseconds);

            activity?.SetTag("inbox.result", "processed");
            activity?.SetTag("inbox.processing.duration.ms", sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "Inbox message processed successfully. InboxMessageId={InboxMessageId}",
                actualInboxMessageId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();

            activity?.SetTag("inbox.result", "cancelled");
            activity?.SetTag("inbox.processing.duration.ms", sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "Inbox message processing cancelled. InboxMessageId={InboxMessageId}",
                actualInboxMessageId);

            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            InboxMetrics.MessagesFailed.Add(1);
            InboxMetrics.ProcessingDurationMs.Record(sw.Elapsed.TotalMilliseconds);

            activity?.SetTag("inbox.result", "failed");
            activity?.SetTag("inbox.processing.duration.ms", sw.Elapsed.TotalMilliseconds);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            await inboxManager.MarkFailedAsync(
                actualInboxMessageId,
                ex.Message,
                ex.GetType().Name);

            _logger.LogWarning(
                ex,
                "Inbox message processing failed. InboxMessageId={InboxMessageId}",
                actualInboxMessageId);
        }
    }
}