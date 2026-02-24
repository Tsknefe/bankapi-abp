using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking;

public class IdempotencyGate
{
    private readonly IRepository<BankingIdempotencyRecord, Guid> _repo;

    public IdempotencyGate(IRepository<BankingIdempotencyRecord, Guid> repo)
    {
        _repo = repo;
    }

    public async Task<(bool IsDuplicate, BankingIdempotencyRecord Record)> TryBeginAsync(
        Guid userId, string operation, string key, string? requestHash)
    {
        var rec = new BankingIdempotencyRecord(Guid.NewGuid(), userId, operation, key, requestHash);

        try
        {
            await _repo.InsertAsync(rec, autoSave: true);
            return (false, rec);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            var existing = await _repo.FirstOrDefaultAsync(x =>
                x.UserId == userId && x.Operation == operation && x.IdempotencyKey == key);

            if (existing == null)
                throw; 

            return (true, existing);
        }
    }

    public async Task<string> GetOrThrowDuplicateResponseAsync(BankingIdempotencyRecord existing)
    {
        if (existing.Status == "Completed" && existing.ResponseJson != null)
            return existing.ResponseJson;

        throw new BusinessException("IDEMPOTENCY_IN_PROGRESS")
            .WithData("message", "This request is already being processed. Please retry.");
    }

    public async Task CompleteAsync(BankingIdempotencyRecord rec, object responseDto, int statusCode = 200)
    {
        var json = JsonSerializer.Serialize(responseDto);
        rec.MarkCompleted(statusCode, json);
        await _repo.UpdateAsync(rec, autoSave: true);
    }

    public async Task FailAsync(BankingIdempotencyRecord rec, Exception ex)
    {
        rec.MarkFailed(ex.Message);
        await _repo.UpdateAsync(rec, autoSave: true);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}