using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace BankApiAbp.Banking.Infrastructure;

public class RetryExecutor : ITransientDependency
{
    private const string DeadlockDetected = "40P01";
    private const string SerializationFailure = "40001";
    private const string LockNotAvailable = "55P03"; 

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxAttempts = 3,
        int baseDelayMs = 60,
        CancellationToken ct = default)
    {
        if (maxAttempts < 1) maxAttempts = 1;
        if (baseDelayMs < 0) baseDelayMs = 0;

        Exception? last = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await action(ct);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex))
            {
                last = ex;

                var delay = baseDelayMs * attempt;
                delay += Random.Shared.Next(0, 35);

                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                last = ex;
                throw;
            }
        }

        throw last ?? new AbpException("RetryExecutor failed with unknown error.");
    }

    public Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        int maxAttempts = 3,
        int baseDelayMs = 60,
        CancellationToken ct = default)
        => ExecuteAsync<object>(async token =>
        {
            await action(token);
            return new object();
        }, maxAttempts, baseDelayMs, ct);

    private static bool IsRetryable(Exception ex)
    {
        if (ex is DbUpdateConcurrencyException) return true;

        if (ex is DbUpdateException dbu && dbu.InnerException is PostgresException pg1)
            return IsRetryableSqlState(pg1.SqlState);

        if (ex is PostgresException pg2)
            return IsRetryableSqlState(pg2.SqlState);

        if (ex.InnerException != null)
            return IsRetryable(ex.InnerException);

        return false;
    }

    private static bool IsRetryableSqlState(string? sqlState)
        => sqlState is DeadlockDetected or SerializationFailure or LockNotAvailable;
}