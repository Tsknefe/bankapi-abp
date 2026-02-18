using System;
using System.ComponentModel.DataAnnotations;
using BankApiAbp.Banking;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Entities;

public class Account : FullAuditedAggregateRoot<Guid>
{
    public Guid CustomerId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Iban { get; private set; } = default!;
    public decimal Balance { get; private set; }
    public AccountType AccountType { get; private set; }
    public bool IsActive { get; private set; } = true;

    [Timestamp]
    public byte[] RowVersion { get; private set; } = default!;

    private Account() { }

    public Account(Guid id, Guid customerId, string name, string iban, AccountType accountType, decimal balance = 0)
        : base(id)
    {
        CustomerId = customerId;
        Name = name;
        Iban = iban;
        AccountType = accountType;
        Balance = balance;
        IsActive = true;
    }

    public void Deposit(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0");
        Balance += amount;
    }

    public void Withdraw(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Amount must be > 0");
        if (!IsActive) throw new InvalidOperationException("Account is not active");
        if (Balance < amount) throw new InvalidOperationException("Insufficient balance");
        Balance -= amount;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
