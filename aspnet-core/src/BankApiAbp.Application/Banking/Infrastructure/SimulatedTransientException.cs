using System;

namespace BankApiAbp.Banking.Infrastructure;

public class SimulatedTransientException : Exception
{
    public SimulatedTransientException()
        : base("Simulated transient failure for retry testing.")
    {
    }
}