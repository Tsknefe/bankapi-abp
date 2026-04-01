using System;

namespace BankApiAbp.Banking.Messaging;

public class InboxExecutionDecision
{
    public bool ShouldProcess { get; set; }

    public bool IsDuplicate { get; set; }

    public bool IsInProgress { get; set; }

    public bool IsDeadLettered { get; set; }

    public Guid? InboxMessageId { get; set; }

    public static InboxExecutionDecision Process(Guid inboxMessageId)
    {
        return new InboxExecutionDecision
        {
            ShouldProcess = true,
            InboxMessageId = inboxMessageId
        };
    }

    public static InboxExecutionDecision Duplicate()
    {
        return new InboxExecutionDecision
        {
            ShouldProcess = false,
            IsDuplicate = true
        };
    }

    public static InboxExecutionDecision InProgress()
    {
        return new InboxExecutionDecision
        {
            ShouldProcess = false,
            IsInProgress = true
        };
    }

    public static InboxExecutionDecision DeadLettered()
    {
        return new InboxExecutionDecision
        {
            ShouldProcess = false,
            IsDeadLettered = true
        };
    }
}