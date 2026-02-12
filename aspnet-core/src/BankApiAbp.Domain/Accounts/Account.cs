using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace BankApiAbp.Accounts;

public class Account : FullAuditedAggregateRoot<Guid>
{
    public string Name { get; private set; } = null!;
    public string Iban { get; private set; } = null!;
    public decimal Balance { get; private set; }
    public AccountType AccountType { get; private set; }
    public bool IsActive { get; private set; } = true;

    public Guid CustomerId { get; private set; }

    private Account() { } 

    public Account(Guid id, Guid customerId, string name, string iban, AccountType accountType, decimal balance = 0m)
        : base(id)
    {
        CustomerId = customerId;
        SetName(name);
        SetIban(iban);
        AccountType = accountType;

        if (balance < 0) throw new ArgumentException("Balance cannot be negative.");
        Balance = balance;
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.");
        Name = name.Trim();
    }

    public void SetIban(string iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) throw new ArgumentException("IBAN is required.");
        Iban = iban.Trim();
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    public void Deposit(decimal amount)
    {
        if (!IsActive) throw new InvalidOperationException("Account is not active.");
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.");
        Balance += amount;
    }

    public void Withdraw(decimal amount)
    {
        if (!IsActive) throw new InvalidOperationException("Account is not active.");
        if (amount <= 0) throw new ArgumentException("Amount must be > 0.");
        if (Balance < amount) throw new InvalidOperationException("Insufficient balance.");
        Balance -= amount;
    }
    public void SpendFromAccount(decimal amount)
    {
        Withdraw(amount);
    }

}
