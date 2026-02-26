using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace BankApiAbp.Banking;

public class IdempotencyGate : ITransientDependency
{
    private readonly IRepository<BankingIdempotencyRecord, Guid> _repo;
    private readonly IUnitOfWorkManager _uow;

    public IdempotencyGate(IRepository<BankingIdempotencyRecord, Guid> repo,
        IUnitOfWorkManager uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<(bool IsDuplicate, BankingIdempotencyRecord Record)> TryBeginAsync(
        Guid userId, string operation, string key, string? requestHash)
    {
        var rec = new BankingIdempotencyRecord(Guid.NewGuid(), userId, operation, key, requestHash);

        try
        {
            using var u1 = _uow.Begin(requiresNew:true, isTransactional:false);

            await _repo.InsertAsync(rec, autoSave: true);

            await u1.CompleteAsync();
            return (false, rec);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
           
        }
        using var u2 = _uow.Begin(requiresNew:true, isTransactional:false);
        var existing = await _repo.FirstOrDefaultAsync(x => x.UserId == userId && x.Operation == operation && x.IdempotencyKey == key); 
        
        await u2.CompleteAsync();

        if (existing == null) 
        {
            throw new BusinessException("IDEMPOTENCY_RECORD_NOT_FOUND");
        }
        return (true, existing); 

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