using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Entities;

public class Customer : FullAuditedAggregateRoot<Guid>
{
    public Guid UserId { get;  private set; }
    public string Name { get; private set; } = default!;
    public string TcNo { get; private set; } = default!;
    public DateTime BirthDate { get; private set; }
    public string BirthPlace { get; private set; } = default!;

    private Customer() { }

    public Customer(Guid id,Guid userId, string name, string tcNo, DateTime birthDate, string birthPlace)
        : base(id)
    {
        UserId = userId;
        Name = name;
        TcNo = tcNo;
        BirthDate = birthDate;
        BirthPlace = birthPlace;
    }
}
