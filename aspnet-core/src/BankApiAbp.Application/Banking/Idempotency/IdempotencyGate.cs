using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using BankApiAbp.EntityFrameworkCore;

namespace BankApiAbp.Banking;

public class IdempotencyGate : ITransientDependency
{
    private readonly IRepository<BankingIdempotencyRecord, Guid> _repo;
    private readonly IUnitOfWorkManager _uow;
    private readonly IDbContextProvider<BankApiAbpDbContext> _dbContextProvider;

    public IdempotencyGate(
        IRepository<BankingIdempotencyRecord, Guid> repo,
        IUnitOfWorkManager uow,
        IDbContextProvider<BankApiAbpDbContext> dbContextProvider)
    {
        _repo = repo;
        _uow = uow;
        _dbContextProvider = dbContextProvider;
    }

    public async Task<(bool IsDuplicate, BankingIdempotencyRecord Record)> TryBeginAsync(
        Guid userId,
        string operation,
        string key,
        string? requestHash)
    {
        var rec = new BankingIdempotencyRecord(
            Guid.NewGuid(),
            userId,
            operation,
            key,
            requestHash
        );

        using var uow = _uow.Begin(requiresNew: true, isTransactional: false);

        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var sql = """
                  INSERT INTO "BankingIdempotencyRecords"
                  (
                      "Id",
                      "ConcurrencyStamp",
                      "CreationTime",
                      "CreatorId",
                      "ErrorMessage",
                      "ExtraProperties",
                      "IdempotencyKey",
                      "LastModificationTime",
                      "LastModifierId",
                      "Operation",
                      "RequestHash",
                      "ResponseJson",
                      "ResponseStatusCode",
                      "Status",
                      "UserId"
                  )
                  VALUES
                  (
                      @id,
                      @concurrencyStamp,
                      @creationTime,
                      @creatorId,
                      @errorMessage,
                      @extraProperties,
                      @idempotencyKey,
                      @lastModificationTime,
                      @lastModifierId,
                      @operation,
                      @requestHash,
                      @responseJson,
                      @responseStatusCode,
                      @status,
                      @userId
                  )
                  ON CONFLICT ("UserId", "Operation", "IdempotencyKey")
                  DO NOTHING;
                  """;

        var affected = await dbContext.Database.ExecuteSqlRawAsync(
            sql,
            new NpgsqlParameter("id", rec.Id),
            new NpgsqlParameter("concurrencyStamp", rec.ConcurrencyStamp ?? string.Empty),
            new NpgsqlParameter("creationTime", rec.CreationTime),
            new NpgsqlParameter("creatorId", rec.CreatorId ?? (object)DBNull.Value),
            new NpgsqlParameter("errorMessage", rec.ErrorMessage ?? (object)DBNull.Value),
            new NpgsqlParameter("extraProperties",JsonSerializer.Serialize(rec.ExtraProperties)),
            new NpgsqlParameter("idempotencyKey", rec.IdempotencyKey),
            new NpgsqlParameter("lastModificationTime", rec.LastModificationTime ?? (object)DBNull.Value),
            new NpgsqlParameter("lastModifierId", rec.LastModifierId ?? (object)DBNull.Value),
            new NpgsqlParameter("operation", rec.Operation),
            new NpgsqlParameter("requestHash", rec.RequestHash ?? (object)DBNull.Value),
            new NpgsqlParameter("responseJson", rec.ResponseJson ?? (object)DBNull.Value),
            new NpgsqlParameter("responseStatusCode",rec.ResponseStatusCode ?? (object)DBNull.Value),
            new NpgsqlParameter("status", rec.Status),
            new NpgsqlParameter("userId", rec.UserId)
        );

        await uow.CompleteAsync();

        if (affected > 0)
        {
            return (false, rec);
        }

        var existing = await FindExistingAsync(userId, operation, key);

        if (existing == null)
        {
            throw new BusinessException("IDEMPOTENCY_RECORD_NOT_FOUND")
                .WithData("message", "Idempotency kaydı bekleniyordu ancak bulunamadı.");
        }

        return (true, existing);
    }

    public Task<string> GetOrThrowDuplicateResponseAsync(BankingIdempotencyRecord existing)
    {
        if (existing.Status == "Completed" && existing.ResponseJson != null)
            return Task.FromResult(existing.ResponseJson);

        throw new BusinessException("IDEMPOTENCY_IN_PROGRESS")
            .WithData("message", "This request is already being processed. Please retry.");
    }

    public async Task CompleteAsync(
        BankingIdempotencyRecord rec,
        object responseDto,
        int statusCode = 200)
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

    private async Task<BankingIdempotencyRecord?> FindExistingAsync(
        Guid userId,
        string operation,
        string key)
    {
        using var uow = _uow.Begin(requiresNew: true, isTransactional: false);

        var existing = await _repo.FirstOrDefaultAsync(
            x => x.UserId == userId
              && x.Operation == operation
              && x.IdempotencyKey == key
        );

        await uow.CompleteAsync();
        return existing;
    }
}