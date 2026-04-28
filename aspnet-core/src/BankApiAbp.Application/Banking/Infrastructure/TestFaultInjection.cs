namespace BankApiAbp.Banking.Infrastructure;

public class TestFaultInjection
{
    private int _remainingTransientFailures;

    public void SetTransientFailureCount(int count)
    {
        _remainingTransientFailures = count;
    }

    public bool ShouldThrowTransientFailure()
    {
        if (_remainingTransientFailures <= 0)
            return false;

        _remainingTransientFailures--;
        return true;
    }

    public void Reset()
    {
        _remainingTransientFailures = 0;
    }
}