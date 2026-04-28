using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using BankApiAbp.Banking.Messaging;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace BankApiAbp.Banking.Messaging.Handlers;

public class TransferAuditLogHandler :
    IDistributedEventHandler<MoneyTransferredEto>,
    ITransientDependency
{
    private const string ConsumerName = nameof(TransferAuditLogHandler);

    private readonly IInboxManager _inboxManager;
    private readonly ITransferEventSideEffectService _sideEffects;
    private readonly ILogger<TransferAuditLogHandler> _logger;

    public TransferAuditLogHandler(
        IInboxManager inboxManager,
        ITransferEventSideEffectService sideEffects,
        ILogger<TransferAuditLogHandler> logger)
    {
        _inboxManager = inboxManager;
        _sideEffects = sideEffects;
        _logger = logger;
    }

    public async Task HandleEventAsync(MoneyTransferredEto eventData)
    {
        var parentContext = default(ActivityContext);

        if (!string.IsNullOrWhiteSpace(eventData.TraceParent))
        {
            ActivityContext.TryParse(
                eventData.TraceParent,
                eventData.TraceState,
                out parentContext);
        }

        using var activity = parentContext != default
            ? InboxTracing.ActivitySource.StartActivity(
                "Inbox.TransferAuditLogHandler",
                ActivityKind.Consumer,
                parentContext)
            : InboxTracing.ActivitySource.StartActivity(
                "Inbox.TransferAuditLogHandler",
                ActivityKind.Consumer);

        activity?.SetTag("consumer.name", ConsumerName);
        activity?.SetTag("event.id", eventData.EventId.ToString());
        activity?.SetTag("event.name", nameof(MoneyTransferredEto));
        activity?.SetTag("transfer.id", eventData.TransferId.ToString());

        var payloadJson = JsonSerializer.Serialize(eventData);

        var decision = await _inboxManager.TryBeginProcessingAsync(
            eventData.EventId,
            nameof(MoneyTransferredEto),
            ConsumerName,
            payloadJson: payloadJson);

        activity?.SetTag("decision.should_process", decision.ShouldProcess);
        activity?.SetTag("decision.is_duplicate", decision.IsDuplicate);
        activity?.SetTag("decision.is_in_progress", decision.IsInProgress);
        activity?.SetTag("decision.is_dead_lettered", decision.IsDeadLettered);

        if (!decision.ShouldProcess)
        {
            _logger.LogInformation(
                "Audit event skipped. EventId={EventId}, Consumer={ConsumerName}",
                eventData.EventId,
                ConsumerName);

            return;
        }

        try
        {
            await _sideEffects.WriteAuditLogAsync(eventData);

            await _inboxManager.MarkProcessedAsync(decision.InboxMessageId!.Value);

            activity?.SetTag("handler.success", true);

            _logger.LogInformation(
                "Audit event processed. EventId={EventId}, Consumer={ConsumerName}",
                eventData.EventId,
                ConsumerName);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            if (decision.InboxMessageId.HasValue)
            {
                await _inboxManager.MarkFailedAsync(
                    decision.InboxMessageId.Value,
                    ex.ToString(),
                    ex.GetType().Name);
            }

            throw;
        }
    }
}