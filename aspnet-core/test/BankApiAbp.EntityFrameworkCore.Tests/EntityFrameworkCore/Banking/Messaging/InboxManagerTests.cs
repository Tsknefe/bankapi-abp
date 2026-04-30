using System;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace BankApiAbp.Banking.Messaging;

public class InboxManagerTests
    : BankApiAbpEntityFrameworkCoreTestBase
{
    private readonly IInboxManager _inboxManager;
    private readonly IRepository<InboxMessage, Guid> _inboxRepository;

    public InboxManagerTests()
    {
        _inboxManager = ServiceProvider.GetRequiredService<IInboxManager>();
        _inboxRepository = ServiceProvider.GetRequiredService<IRepository<InboxMessage, Guid>>();
    }

    [Fact]
    public async Task Should_Create_New_Inbox_Message_And_Return_Process()
    {
        await ClearInboxAsync();

        var eventId = Guid.NewGuid();

        var decision = await _inboxManager.TryBeginProcessingAsync(
            eventId,
            "MoneyTransferredEto",
            "TransferAuditLogHandler",
            payloadHash: "hash-1",
            payloadJson: "{\"event\":\"money-transferred\"}",
            maxRetryCount: 3);

        Assert.True(decision.ShouldProcess);
        Assert.False(decision.IsDuplicate);
        Assert.False(decision.IsInProgress);
        Assert.False(decision.IsDeadLettered);
        Assert.NotNull(decision.InboxMessageId);

        var created = await _inboxRepository.GetAsync(decision.InboxMessageId!.Value);

        Assert.Equal(eventId, created.EventId);
        Assert.Equal("MoneyTransferredEto", created.EventName);
        Assert.Equal("TransferAuditLogHandler", created.ConsumerName);
        Assert.Equal(InboxMessageStatus.Processing, created.Status);
        Assert.Equal("hash-1", created.PayloadHash);
        Assert.Equal("{\"event\":\"money-transferred\"}", created.PayloadJson);
        Assert.Equal(0, created.RetryCount);
        Assert.Equal(3, created.MaxRetryCount);
    }

    [Fact]
    public async Task Should_Return_Duplicate_When_Message_Is_Already_Processed()
    {
        await ClearInboxAsync();

        var message = await InsertInboxAsync(
            status: InboxMessageStatus.Processed,
            consumerName: "TransferAuditLogHandler");

        var decision = await _inboxManager.TryBeginProcessingAsync(
            message.EventId,
            message.EventName,
            message.ConsumerName);

        Assert.False(decision.ShouldProcess);
        Assert.True(decision.IsDuplicate);
        Assert.False(decision.IsInProgress);
        Assert.False(decision.IsDeadLettered);
        Assert.Null(decision.InboxMessageId);
    }

    [Fact]
    public async Task Should_Return_InProgress_When_Message_Is_Already_Processing()
    {
        await ClearInboxAsync();

        var message = await InsertInboxAsync(
            status: InboxMessageStatus.Processing,
            consumerName: "TransferAuditLogHandler");

        var decision = await _inboxManager.TryBeginProcessingAsync(
            message.EventId,
            message.EventName,
            message.ConsumerName);

        Assert.False(decision.ShouldProcess);
        Assert.False(decision.IsDuplicate);
        Assert.True(decision.IsInProgress);
        Assert.False(decision.IsDeadLettered);
        Assert.Null(decision.InboxMessageId);
    }

    [Fact]
    public async Task Should_Return_DeadLettered_When_Message_Is_DeadLettered()
    {
        await ClearInboxAsync();

        var message = await InsertInboxAsync(
            status: InboxMessageStatus.DeadLettered,
            consumerName: "TransferAuditLogHandler");

        var decision = await _inboxManager.TryBeginProcessingAsync(
            message.EventId,
            message.EventName,
            message.ConsumerName);

        Assert.False(decision.ShouldProcess);
        Assert.False(decision.IsDuplicate);
        Assert.False(decision.IsInProgress);
        Assert.True(decision.IsDeadLettered);
        Assert.Null(decision.InboxMessageId);
    }

    [Fact]
    public async Task Should_Move_Pending_Message_To_Processing()
    {
        await ClearInboxAsync();

        var message = await InsertInboxAsync(
            status: InboxMessageStatus.Pending,
            consumerName: "TransferAuditLogHandler");

        var decision = await _inboxManager.TryBeginProcessingAsync(
            message.EventId,
            message.EventName,
            message.ConsumerName);

        Assert.True(decision.ShouldProcess);
        Assert.NotNull(decision.InboxMessageId);

        var updated = await _inboxRepository.GetAsync(message.Id);

        Assert.Equal(InboxMessageStatus.Processing, updated.Status);
        Assert.NotNull(updated.LastAttemptTime);
    }

    [Fact]
    public async Task Should_Move_Failed_Message_To_Processing()
    {
        await ClearInboxAsync();

        var message = await InsertInboxAsync(
            status: InboxMessageStatus.Failed,
            consumerName: "TransferAuditLogHandler");

        var oldRetryCount = message.RetryCount;

        var decision = await _inboxManager.TryBeginProcessingAsync(
            message.EventId,
            message.EventName,
            message.ConsumerName);

        Assert.True(decision.ShouldProcess);
        Assert.NotNull(decision.InboxMessageId);

        var updated = await _inboxRepository.GetAsync(message.Id);

        Assert.Equal(InboxMessageStatus.Processing, updated.Status);
        Assert.Equal(oldRetryCount, updated.RetryCount);
        Assert.NotNull(updated.LastAttemptTime);
    }

    [Fact]
    public async Task Should_Move_Retrying_Message_To_Processing()
    {
        await ClearInboxAsync();

        var message = await InsertInboxAsync(
            status: InboxMessageStatus.Retrying,
            consumerName: "TransferAuditLogHandler");

        var decision = await _inboxManager.TryBeginProcessingAsync(
            message.EventId,
            message.EventName,
            message.ConsumerName);

        Assert.True(decision.ShouldProcess);
        Assert.NotNull(decision.InboxMessageId);

        var updated = await _inboxRepository.GetAsync(message.Id);

        Assert.Equal(InboxMessageStatus.Processing, updated.Status);
        Assert.NotNull(updated.LastAttemptTime);
    }

    [Fact]
    public async Task Should_Mark_Message_As_Processed()
    {
        await ClearInboxAsync();

        var message = await InsertInboxAsync(
            status: InboxMessageStatus.Processing,
            consumerName: "TransferAuditLogHandler");

        await _inboxManager.MarkProcessedAsync(message.Id);

        var updated = await _inboxRepository.GetAsync(message.Id);

        Assert.Equal(InboxMessageStatus.Processed, updated.Status);
        Assert.NotNull(updated.ProcessedAt);
        Assert.NotNull(updated.LastAttemptTime);
        Assert.Null(updated.Error);
        Assert.Null(updated.LastErrorCode);
    }

    [Fact]
    public async Task Should_Mark_Message_As_Retrying_When_Retry_Quota_Exists()
    {
        await ClearInboxAsync();

        var message = new InboxMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "MoneyTransferredEto",
            "TransferAuditLogHandler",
            payloadHash: "hash",
            payloadJson: "{\"test\":true}",
            maxRetryCount: 3);

        message.MarkProcessing();
        await _inboxRepository.InsertAsync(message, autoSave: true);

        await _inboxManager.MarkFailedAsync(message.Id, "temporary failure", "Exception");

        var updated = await _inboxRepository.GetAsync(message.Id);

        Assert.Equal(InboxMessageStatus.Retrying, updated.Status);
        Assert.Equal(1, updated.RetryCount);
        Assert.Equal("temporary failure", updated.Error);
        Assert.Equal("Exception", updated.LastErrorCode);
        Assert.NotNull(updated.NextRetryTime);
    }

    [Fact]
    public async Task Should_Mark_Message_As_DeadLettered_When_Retry_Quota_Is_Exceeded()
    {
        await ClearInboxAsync();

        var message = new InboxMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "MoneyTransferredEto",
            "TransferAuditLogHandler",
            payloadHash: "hash",
            payloadJson: "{\"test\":true}",
            maxRetryCount: 1);

        message.MarkProcessing();
        await _inboxRepository.InsertAsync(message, autoSave: true);

        await _inboxManager.MarkFailedAsync(message.Id, "first failure", "Exception");

        var firstUpdate = await _inboxRepository.GetAsync(message.Id);
        Assert.Equal(InboxMessageStatus.Retrying, firstUpdate.Status);
        Assert.Equal(1, firstUpdate.RetryCount);

        await _inboxManager.MarkFailedAsync(message.Id, "second failure", "Exception");

        var finalUpdate = await _inboxRepository.GetAsync(message.Id);

        Assert.Equal(InboxMessageStatus.DeadLettered, finalUpdate.Status);
        Assert.Equal(2, finalUpdate.RetryCount);
        Assert.Equal("Exception", finalUpdate.LastErrorCode);
        Assert.NotNull(finalUpdate.DeadLetteredAt);
        Assert.Equal("Max retry exceeded", finalUpdate.DeadLetterReason);
    }

    [Fact]
    public async Task Should_Requeue_Message_As_Pending()
    {
        await ClearInboxAsync();

        var message = await InsertInboxAsync(
            status: InboxMessageStatus.DeadLettered,
            consumerName: "TransferAuditLogHandler");

        await _inboxManager.RequeueAsync(message.Id);

        var updated = await _inboxRepository.GetAsync(message.Id);

        Assert.Equal(InboxMessageStatus.Pending, updated.Status);
        Assert.Null(updated.Error);
        Assert.Null(updated.LastErrorCode);
        Assert.Null(updated.NextRetryTime);
        Assert.Null(updated.DeadLetterReason);
        Assert.Null(updated.DeadLetteredAt);
    }

    private async Task ClearInboxAsync()
    {
        var list = await _inboxRepository.GetListAsync();
        if (list.Any())
        {
            await _inboxRepository.DeleteManyAsync(list, autoSave: true);
        }
    }

    private async Task<InboxMessage> InsertInboxAsync(
        string status,
        string consumerName)
    {
        var message = new InboxMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "MoneyTransferredEto",
            consumerName,
            payloadHash: "hash",
            payloadJson: "{\"event\":\"money-transferred\"}",
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
}