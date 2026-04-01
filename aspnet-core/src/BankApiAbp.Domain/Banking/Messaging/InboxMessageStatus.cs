namespace BankApiAbp.Banking.Messaging;

public static class InboxMessageStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Processed = "Processed";
    public const string Retrying = "Retrying";
    public const string Failed = "Failed";
    public const string DeadLettered = "DeadLettered";
}