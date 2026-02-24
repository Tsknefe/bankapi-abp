using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Banking;

public class BankingIdempotencyRecord : AuditedAggregateRoot<Guid>
{
    public Guid UserId { get; private set; }
    public string Operation { get; private set; } = null!;         
    public string IdempotencyKey { get; private set; } = null!;    
    public string? RequestHash { get; private set; }               
    public string Status { get; private set; } = "Processing";     
    public int? ResponseStatusCode { get; private set; }
    public string? ResponseJson { get; private set; }
    public string? ErrorMessage { get; private set; }

    protected BankingIdempotencyRecord() { }

    public BankingIdempotencyRecord(Guid id, Guid userId, string operation, string key, string? requestHash)
        : base(id)
    {
        UserId = userId;
        Operation = operation;
        IdempotencyKey = key;
        RequestHash = requestHash;
    }

    public void MarkCompleted(int statusCode, string responseJson)
    {
        Status = "Completed";
        ResponseStatusCode = statusCode;
        ResponseJson = responseJson;
        ErrorMessage = null;
    }

    public void MarkFailed(string error)
    {
        Status = "Failed";
        ErrorMessage = error;
    }
}