using System.Diagnostics;

namespace BankApiAbp.Banking.Messaging;

public static class InboxTracing
{
    public const string ActivitySourceName = "BankApiAbp.Banking.Inbox";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}