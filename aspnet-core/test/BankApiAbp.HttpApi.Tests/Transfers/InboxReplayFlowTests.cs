using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using BankApiAbp.Banking.Messaging;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Transfers;

public class InboxReplayFlowTests
{
    [Fact]
    public async Task Should_List_Only_Retryable_Inbox_Messages()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        using var scope = TestClientFactory.CreateScope();
        var inboxRepository = scope.ServiceProvider
            .GetRequiredService<IRepository<InboxMessage, Guid>>();

        await ClearInboxAsync(inboxRepository);

        await InsertInboxAsync(inboxRepository, InboxMessageStatus.Processed, "TransferAuditLogHandler");
        var failed = await InsertInboxAsync(inboxRepository, InboxMessageStatus.Failed, "TransferAuditLogHandler");
        var retrying = await InsertInboxAsync(inboxRepository, InboxMessageStatus.Retrying, "TransferNotificationHandler");
        var deadLettered = await InsertInboxAsync(inboxRepository, InboxMessageStatus.DeadLettered, "TransferAuditLogHandler");

        var response = await client.GetAsync("/api/app/event-admin/retryable-inbox-messages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<InboxMessageDtoForHttpTest[]>();
        result.Should().NotBeNull();


        var list = result!.ToList();

        list.Should().HaveCount(3);
        list.Should().Contain(x => x.Id == failed.Id && x.Status == InboxMessageStatus.Failed);
        list.Should().Contain(x => x.Id == retrying.Id && x.Status == InboxMessageStatus.Retrying);
        list.Should().Contain(x => x.Id == deadLettered.Id && x.Status == InboxMessageStatus.DeadLettered);
        list.Should().NotContain(x => x.Status == InboxMessageStatus.Processed);
    }

    [Fact]
    public async Task Should_Replay_A_Failed_Inbox_Message_And_Mark_It_Processed()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        using var scope = TestClientFactory.CreateScope();

        var inboxRepository = scope.ServiceProvider
            .GetRequiredService<IRepository<InboxMessage, Guid>>();

        var auditRepository = scope.ServiceProvider
            .GetRequiredService<IRepository<TransferAuditLog, Guid>>();

        await ClearInboxAsync(inboxRepository);
        await ClearAuditAsync(auditRepository);

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

        await inboxRepository.InsertAsync(inbox, autoSave: true);

        var response = await client.PostAsync(
            $"/api/app/event-admin/retry-inbox-message/{inbox.Id}",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var verifyScope = TestClientFactory.CreateScope();

        var verifyRepo = verifyScope.ServiceProvider
            .GetRequiredService<IRepository<InboxMessage, Guid>>();

        var updatedInbox = await verifyRepo.GetAsync(inbox.Id);
        updatedInbox.Status.Should().Be(InboxMessageStatus.Processed);

        using var verifyScope2 = TestClientFactory.CreateScope();

        var verifyAuditRepo = verifyScope2.ServiceProvider
            .GetRequiredService<IRepository<TransferAuditLog, Guid>>();

        var auditLogs = await verifyAuditRepo.GetListAsync();
        auditLogs.Should().Contain(x =>
            x.EventId == eventData.EventId &&
            x.TransferId == eventData.TransferId);
    }

    private static async Task ClearInboxAsync(IRepository<InboxMessage, Guid> repository)
    {
        var list = await repository.GetListAsync();
        foreach (var item in list)
        {
            await repository.DeleteAsync(item, autoSave: true);
        }
    }

    private static async Task ClearAuditAsync(IRepository<TransferAuditLog, Guid> repository)
    {
        var list = await repository.GetListAsync();
        foreach (var item in list)
        {
            await repository.DeleteAsync(item, autoSave: true);
        }
    }

    private static async Task<InboxMessage> InsertInboxAsync(
        IRepository<InboxMessage, Guid> repository,
        string status,
        string consumerName)
    {
        var eventData = CreateMoneyTransferredEto();

        var message = new InboxMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            nameof(MoneyTransferredEto),
            consumerName,
            payloadHash: "hash",
            payloadJson: JsonSerializer.Serialize(eventData),
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

        await repository.InsertAsync(message, autoSave: true);
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
            Description = "http replay test",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = DateTime.UtcNow
        };
    }

    private class InboxMessageDtoForHttpTest
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public string EventName { get; set; } = string.Empty;
        public string ConsumerName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public int MaxRetryCount { get; set; }
        public DateTime? LastAttemptTime { get; set; }
        public DateTime? NextRetryTime { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? Error { get; set; }
        public string? LastErrorCode { get; set; }
        public DateTime? DeadLetteredAt { get; set; }
        public string? DeadLetterReason { get; set; }
    }
}