using System;
using BankApiAbp.Banking.Messaging;
using FluentAssertions;
using Shouldly;
using Xunit;

namespace BankApiAbp.Banking.Messaging;

public class InboxMessageTests
{
    [Fact]
    public void Should_Start_As_Pending()
    {
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var message = new InboxMessage(
            id,
            eventId,
            nameof(MoneyTransferredEto),
            nameof(TransferAuditLogHandler),
            payloadHash: "hash-1",
            payloadJson: "{\"test\":true}",
            maxRetryCount: 3);

        message.EventId.Should().Be(eventId);
        message.EventName.Should().Be(nameof(MoneyTransferredEto));
        message.ConsumerName.Should().Be(nameof(TransferAuditLogHandler));
        message.Status.Should().Be(InboxMessageStatus.Pending);
        message.PayloadHash.Should().Be("hash-1");
        message.PayloadJson.Should().Be("{\"test\":true}");
        message.RetryCount.Should().Be(0);
        message.MaxRetryCount.Should().Be(3);
        message.ProcessedAt.Should().BeNull();
        message.LastAttemptTime.Should().BeNull();
        message.NextRetryTime.Should().BeNull();
        message.Error.Should().BeNull();
        message.LastErrorCode.Should().BeNull();
        message.DeadLetteredAt.Should().BeNull();
        message.DeadLetterReason.Should().BeNull();
    }

    [Fact]
    public void Should_Mark_As_Processing()
    {
        var message = CreateMessage();

        message.MarkProcessing();

        message.Status.Should().Be(InboxMessageStatus.Processing);
        message.LastAttemptTime.Should().NotBeNull();
        message.Error.Should().BeNull();
        message.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void Should_Mark_As_Processed()
    {
        var message = CreateMessage();

        message.MarkProcessing();
        message.MarkProcessed();

        message.Status.Should().Be(InboxMessageStatus.Processed);
        message.ProcessedAt.Should().NotBeNull();
        message.LastAttemptTime.Should().NotBeNull();
        message.Error.Should().BeNull();
        message.LastErrorCode.Should().BeNull();
        message.NextRetryTime.Should().BeNull();
        message.DeadLetteredAt.Should().BeNull();
        message.DeadLetterReason.Should().BeNull();
    }

    [Fact]
    public void Should_Mark_As_Retrying_And_Increment_RetryCount()
    {
        var message = CreateMessage();

        var before = DateTime.UtcNow;
        message.MarkRetry("temporary failure", "TimeoutException", TimeSpan.FromMinutes(2));
        var after = DateTime.UtcNow;

        message.Status.Should().Be(InboxMessageStatus.Retrying);
        message.Error.Should().Be("temporary failure");
        message.LastErrorCode.Should().Be("TimeoutException");
        message.RetryCount.Should().Be(1);
        message.LastAttemptTime.Should().NotBeNull();
        message.NextRetryTime.Should().NotBeNull();
        message.NextRetryTime.Should().BeOnOrAfter(before.AddMinutes(2).AddSeconds(-1));
        message.NextRetryTime.Should().BeOnOrBefore(after.AddMinutes(2).AddSeconds(1));
    }

    [Fact]
    public void Should_Mark_As_Failed_And_Increment_RetryCount()
    {
        var message = CreateMessage();

        message.MarkFailed("db write failed", "DbUpdateException");

        message.Status.Should().Be(InboxMessageStatus.Failed);
        message.Error.Should().Be("db write failed");
        message.LastErrorCode.Should().Be("DbUpdateException");
        message.RetryCount.Should().Be(1);
        message.LastAttemptTime.Should().NotBeNull();
    }

    [Fact]
    public void Should_Mark_As_DeadLettered()
    {
        var message = CreateMessage();

        message.MarkDeadLettered("max retry reached", "Exception", "Max retry exceeded");

        message.Status.Should().Be(InboxMessageStatus.DeadLettered);
        message.Error.Should().Be("max retry reached");
        message.LastErrorCode.Should().Be("Exception");
        message.DeadLetterReason.Should().Be("Max retry exceeded");
        message.DeadLetteredAt.Should().NotBeNull();
        message.LastAttemptTime.Should().NotBeNull();
        message.NextRetryTime.Should().BeNull();
        message.RetryCount.Should().Be(1);
    }

    [Fact]
    public void Should_Requeue_And_Reset_Error_State()
    {
        var message = CreateMessage();

        message.MarkDeadLettered("some error", "Exception", "Dead letter");
        message.Requeue();

        message.Status.Should().Be(InboxMessageStatus.Pending);
        message.Error.Should().BeNull();
        message.LastErrorCode.Should().BeNull();
        message.NextRetryTime.Should().BeNull();
        message.DeadLetterReason.Should().BeNull();
        message.DeadLetteredAt.Should().BeNull();
    }

    [Fact]
    public void Should_Return_True_When_Retry_Quota_Exists()
    {
        var message = CreateMessage(maxRetryCount: 3);

        message.HasRetryQuota().Should().BeTrue();

        message.MarkRetry("e1", "Exception", TimeSpan.FromSeconds(1));
        message.MarkRetry("e2", "Exception", TimeSpan.FromSeconds(1));

        message.HasRetryQuota().Should().BeTrue();
    }

    [Fact]
    public void Should_Return_False_When_Retry_Quota_Exceeded()
    {
        var message = CreateMessage(maxRetryCount: 2);

        message.MarkRetry("e1", "Exception", TimeSpan.FromSeconds(1));
        message.MarkRetry("e2", "Exception", TimeSpan.FromSeconds(1));

        message.HasRetryQuota().Should().BeFalse();
    }

    [Fact]
    public void Should_Truncate_Long_Error_Message()
    {
        var message = CreateMessage();
        var longError = new string('x', 5000);

        message.MarkFailed(longError, "Exception");

        message.Error.Should().NotBeNull();
        message.Error!.Length.Should().Be(4000);
    }

    [Fact]
    public void Should_Truncate_Long_DeadLetter_Reason()
    {
        var message = CreateMessage();
        var longReason = new string('r', 1500);

        message.MarkDeadLettered("error", "Exception", longReason);

        message.DeadLetterReason.Should().NotBeNull();
        message.DeadLetterReason!.Length.Should().Be(1000);
    }

    private static InboxMessage CreateMessage(int maxRetryCount = 3)
    {
        return new InboxMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            nameof(MoneyTransferredEto),
            nameof(TransferAuditLogHandler),
            payloadHash: "hash",
            payloadJson: "{\"event\":\"money-transferred \"}",
            maxRetryCount: maxRetryCount);
    }

    private sealed class TransferAuditLogHandler
    {
    }
}