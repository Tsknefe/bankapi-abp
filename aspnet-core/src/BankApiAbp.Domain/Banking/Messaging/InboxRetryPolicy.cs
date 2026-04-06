using System;

namespace BankApiAbp.Banking.Messaging;

public static class InboxRetryPolicy
{
    public static TimeSpan GetDelay(int retryCount)
    {
        return retryCount switch
        {
            1 => TimeSpan.FromSeconds(30),
            2 => TimeSpan.FromMinutes(2),
            3 => TimeSpan.FromMinutes(10),
            _ => TimeSpan.FromMinutes(30)
        };
    }
}