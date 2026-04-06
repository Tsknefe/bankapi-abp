using System;
using System.Threading.Tasks;

namespace BankApiAbp.Banking.Messaging;

public interface IInboxManager
{
    Task<InboxExecutionDecision> TryBeginProcessingAsync(
        Guid eventId,
        string eventName,
        string consumerName,
        string? payloadHash = null,
        string? payloadJson = null,
        int maxRetryCount = 3);

    Task MarkProcessedAsync(Guid inboxMessageId);

    Task MarkFailedAsync(Guid inboxMessageId, string error, string? errorCode = null);

    Task RequeueAsync(Guid inboxMessageId);
}