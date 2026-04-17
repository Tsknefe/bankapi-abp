using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BankApiAbp.Banking;
using BankApiAbp.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace BankApiAbp.Banking.Messaging;

public class EventAdminAppServiceTests
    : BankApiAbpEntityFrameworkCoreTestBase
{
    private readonly IEventAdminAppService _eventAdminAppService;
    private readonly IRepository<InboxMessage, Guid> _inboxRepository;
    private readonly IRepository<TransferAuditLog, Guid> _auditRepository;
    private readonly IRepository<TransferNotificationLog, Guid> _notificationRepository;

    public EventAdminAppServiceTests()
    {
        _eventAdminAppService = ServiceProvider.GetRequiredService<IEventAdminAppService>();
        _inboxRepository = ServiceProvider.GetRequiredService<IRepository<InboxMessage, Guid>>();
        _auditRepository = ServiceProvider.GetRequiredService<IRepository<TransferAuditLog, Guid>>();
        _notificationRepository = ServiceProvider.GetRequiredService<IRepository<TransferNotificationLog, Guid>>();
    }

    [Fact]
    public async Task Should_Return_Only_Retryable_Messages()
    {
        await ClearAllAsync();

        await InsertInboxWithStatusAsync(InboxMessageStatus.Processed, "TransferAuditLogHandler");
        var failed = await InsertInboxWithStatusAsync(InboxMessageStatus.Failed, "TransferAuditLogHandler");
        var retrying = await InsertInboxWithStatusAsync(InboxMessageStatus.Retrying, "TransferNotificationHandler");
        var deadLettered = await InsertInboxWithStatusAsync(InboxMessageStatus.DeadLettered, "TransferAuditLogHandler");

        var result = await _eventAdminAppService.GetRetryableInboxMessagesAsync();

        Assert.NotNull(result);
        Assert.Equal(3, result.Count());

        Assert.Contains(result, x => x.Id == failed.Id && x.Status == InboxMessageStatus.Failed);
        Assert.Contains(result, x => x.Id == retrying.Id && x.Status == InboxMessageStatus.Retrying);
        Assert.Contains(result, x => x.Id == deadLettered.Id && x.Status == InboxMessageStatus.DeadLettered);
        Assert.DoesNotContain(result, x => x.Status == InboxMessageStatus.Processed);
    }

    [Fact]
    public async Task Should_Throw_When_Retrying_NonRetryable_Message()
    {
        await ClearAllAsync();

        var processed = await InsertInboxWithStatusAsync(
            InboxMessageStatus.Processed,
            "TransferAuditLogHandler");

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => _eventAdminAppService.RetryInboxMessageAsync(processed.Id));

        Assert.Contains("not retryable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_Requeue_And_Dispatch_Audit_Message()
    {
        await ClearAllAsync();

        var eventData = CreateMoneyTransferredEto();
        var payloadJson = JsonSerializer.Serialize(eventData);

        var inbox = new InboxMessage(
            Guid.NewGuid(),
            eventData.EventId,
            nameof(MoneyTransferredEto),
            "TransferAuditLogHandler",
            payloadHash: "hash",
            payloadJson: payloadJson,
            maxRetryCount: 3);

        inbox.MarkProcessing();
        inbox.MarkFailed("simulated failure", "Exception");

        await _inboxRepository.InsertAsync(inbox, autoSave: true);

        await _eventAdminAppService.RetryInboxMessageAsync(inbox.Id);

        var updated = await _inboxRepository.GetAsync(inbox.Id);

        Assert.Equal(InboxMessageStatus.Processed, updated.Status);

        var auditLogs = await _auditRepository.GetListAsync();
        var created = auditLogs.FirstOrDefault(x =>
            x.EventId == eventData.EventId &&
            x.TransferId == eventData.TransferId);

        Assert.NotNull(created);
    }

    [Fact]
    public async Task Should_Requeue_And_Dispatch_Notification_Message()
    {
        await ClearAllAsync();

        var eventData = CreateMoneyTransferredEto();
        var payloadJson = JsonSerializer.Serialize(eventData);

        var inbox = new InboxMessage(
            Guid.NewGuid(),
            eventData.EventId,
            nameof(MoneyTransferredEto),
            "TransferNotificationHandler",
            payloadHash: "hash",
            payloadJson: payloadJson,
            maxRetryCount: 3);

        inbox.MarkProcessing();
        inbox.MarkDeadLettered("dead letter failure", "Exception", "Max retry exceeded");

        await _inboxRepository.InsertAsync(inbox, autoSave: true);

        await _eventAdminAppService.RetryInboxMessageAsync(inbox.Id);

        var updated = await _inboxRepository.GetAsync(inbox.Id);

        Assert.Equal(InboxMessageStatus.Processed, updated.Status);

        var notificationLogs = await _notificationRepository.GetListAsync();
        var created = notificationLogs.FirstOrDefault(x =>
            x.EventId == eventData.EventId &&
            x.TransferId == eventData.TransferId);

        Assert.NotNull(created);
    }

    private async Task ClearAllAsync()
    {
        await DeleteAllAsync(_auditRepository);
        await DeleteAllAsync(_notificationRepository);
        await DeleteAllAsync(_inboxRepository);
    }

    private static async Task DeleteAllAsync<TEntity>(
        IRepository<TEntity, Guid> repository)
        where TEntity : class, IEntity<Guid>
    {
        var list = await repository.GetListAsync();
        foreach (var item in list)
        {
            await repository.DeleteAsync(item, autoSave: true);
        }
    }

    private async Task<InboxMessage> InsertInboxWithStatusAsync(
        string status,
        string consumerName)
    {
        var message = new InboxMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            nameof(MoneyTransferredEto),
            consumerName,
            payloadHash: "hash",
            payloadJson: JsonSerializer.Serialize(CreateMoneyTransferredEto()),
            maxRetryCount: 3);

        switch (status)
        {
            case "Pending":
                message.MarkPending();
                break;

            case "Processing":
                message.MarkProcessing();
                break;

            case "Processed":
                message.MarkProcessing();
                message.MarkProcessed();
                break;

            case "Failed":
                message.MarkProcessing();
                message.MarkFailed("failed", "Exception");
                break;

            case "Retrying":
                message.MarkProcessing();
                message.MarkRetry("retrying", "Exception", TimeSpan.FromMinutes(1));
                break;

            case "DeadLettered":
                message.MarkProcessing();
                message.MarkDeadLettered("dead", "Exception", "Dead letter");
                break;

            default:
                throw new InvalidOperationException($"Unsupported status: {status}");
        }

        await _inboxRepository.InsertAsync(message, autoSave: true);
        return message;
    }

    private static MoneyTransferredEto CreateMoneyTransferredEto()
    {
        return new MoneyTransferredEto
        {
            EventId = Guid.NewGuid(),
            TransferId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            FromAccountId = Guid.NewGuid(),
            ToAccountId = Guid.NewGuid(),
            Amount = 150m,
            Description = "event-admin replay test",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = DateTime.UtcNow
        };
    }
}