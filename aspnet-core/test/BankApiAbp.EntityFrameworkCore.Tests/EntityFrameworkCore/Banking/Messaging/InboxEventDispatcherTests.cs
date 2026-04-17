using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BankApiAbp.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace BankApiAbp.Banking.Messaging;

public class InboxEventDispatcherTests
    : BankApiAbpEntityFrameworkCoreTestBase
{
    private readonly IInboxEventDispatcher _dispatcher;
    private readonly IRepository<InboxMessage, Guid> _inboxRepository;
    private readonly IRepository<TransferAuditLog, Guid> _auditRepository;
    private readonly IRepository<TransferNotificationLog, Guid> _notificationRepository;

    public InboxEventDispatcherTests()
    {
        _dispatcher = ServiceProvider.GetRequiredService<IInboxEventDispatcher>();
        _inboxRepository = ServiceProvider.GetRequiredService<IRepository<InboxMessage, Guid>>();
        _auditRepository = ServiceProvider.GetRequiredService<IRepository<TransferAuditLog, Guid>>();
        _notificationRepository = ServiceProvider.GetRequiredService<IRepository<TransferNotificationLog, Guid>>();
    }

    [Fact]
    public async Task Should_Throw_When_Payload_Is_Empty()
    {
        await ClearAllAsync();

        var inbox = new InboxMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "MoneyTransferredEto",
            "TransferAuditLogHandler",
            payloadHash: "hash",
            payloadJson: null,
            maxRetryCount: 3);

        await _inboxRepository.InsertAsync(inbox, autoSave: true);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => _dispatcher.DispatchAsync(inbox.Id));

        Assert.Equal("Inbox message payload is empty.", ex.Message);
    }

    [Fact]
    public async Task Should_Throw_When_Event_Name_Is_Not_Supported()
    {
        await ClearAllAsync();

        var payloadJson = JsonSerializer.Serialize(CreateMoneyTransferredEto());

        var inbox = new InboxMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "UnsupportedEvent",
            "TransferAuditLogHandler",
            payloadHash: "hash",
            payloadJson: payloadJson,
            maxRetryCount: 3);

        await _inboxRepository.InsertAsync(inbox, autoSave: true);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => _dispatcher.DispatchAsync(inbox.Id));

        Assert.Equal("Unsupported event type: UnsupportedEvent", ex.Message);
    }

    [Fact]
    public async Task Should_Throw_When_Consumer_Is_Not_Supported()
    {
        await ClearAllAsync();

        var eventData = CreateMoneyTransferredEto();
        var payloadJson = JsonSerializer.Serialize(eventData);

        var inbox = new InboxMessage(
            Guid.NewGuid(),
            eventData.EventId,
            "MoneyTransferredEto",
            "UnknownConsumerHandler",
            payloadHash: "hash",
            payloadJson: payloadJson,
            maxRetryCount: 3);

        await _inboxRepository.InsertAsync(inbox, autoSave: true);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => _dispatcher.DispatchAsync(inbox.Id));

        Assert.Equal("Unsupported consumer: UnknownConsumerHandler", ex.Message);
    }

    [Fact]
    public async Task Should_Dispatch_To_Audit_Handler()
    {
        await ClearAllAsync();

        var eventData = CreateMoneyTransferredEto();
        var payloadJson = JsonSerializer.Serialize(eventData);

        var inbox = new InboxMessage(
            Guid.NewGuid(),
            eventData.EventId,
            "MoneyTransferredEto",
            "TransferAuditLogHandler",
            payloadHash: "hash",
            payloadJson: payloadJson,
            maxRetryCount: 3);

        await _inboxRepository.InsertAsync(inbox, autoSave: true);

        await _dispatcher.DispatchAsync(inbox.Id);

        var inboxUpdated = await _inboxRepository.GetAsync(inbox.Id);
        Assert.Equal(InboxMessageStatus.Processed, inboxUpdated.Status);

        var auditLogs = await _auditRepository.GetListAsync();
        var created = auditLogs.FirstOrDefault(x =>
            x.EventId == eventData.EventId &&
            x.TransferId == eventData.TransferId);

        Assert.NotNull(created);
    }

    [Fact]
    public async Task Should_Dispatch_To_Notification_Handler()
    {
        await ClearAllAsync();

        var eventData = CreateMoneyTransferredEto();
        var payloadJson = JsonSerializer.Serialize(eventData);

        var inbox = new InboxMessage(
            Guid.NewGuid(),
            eventData.EventId,
            "MoneyTransferredEto",
            "TransferNotificationHandler",
            payloadHash: "hash",
            payloadJson: payloadJson,
            maxRetryCount: 3);

        await _inboxRepository.InsertAsync(inbox, autoSave: true);

        await _dispatcher.DispatchAsync(inbox.Id);

        var inboxUpdated = await _inboxRepository.GetAsync(inbox.Id);
        Assert.Equal(InboxMessageStatus.Processed, inboxUpdated.Status);

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
            Description = "dispatcher test transfer",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = DateTime.UtcNow
        };
    }
}