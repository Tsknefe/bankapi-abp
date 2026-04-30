using System.Diagnostics.Metrics;

namespace BankApiAbp.Banking.Messaging;

public static class InboxMetrics
{
    public const string MeterName = "BankApiAbp.Banking.Inbox";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> MessagesPicked =
        Meter.CreateCounter<long>("inbox.messages.picked");

    public static readonly Counter<long> MessagesProcessed =
        Meter.CreateCounter<long>("inbox.messages.processed");

    public static readonly Counter<long> MessagesFailed =
        Meter.CreateCounter<long>("inbox.messages.failed");

    public static readonly Counter<long> MessagesRetried =
        Meter.CreateCounter<long>("inbox.messages.retried");

    public static readonly Counter<long> MessagesDeadLettered =
        Meter.CreateCounter<long>("inbox.messages.deadlettered");

    public static readonly Histogram<double> ProcessingDurationMs =
        Meter.CreateHistogram<double>("inbox.processing.duration.ms");
}