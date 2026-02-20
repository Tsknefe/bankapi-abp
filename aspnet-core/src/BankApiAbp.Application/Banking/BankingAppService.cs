using System;
using BankApiAbp.Entities;
using BankApiAbp.Cards;
using BankApiAbp.Transactions;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking;

public partial class BankingAppService : ApplicationService, IBankingAppService
{
    private readonly IRepository<Customer, Guid> _customers;
    private readonly IRepository<Account, Guid> _accounts;
    private readonly IRepository<DebitCard, Guid> _debitCards;
    private readonly IRepository<CreditCard, Guid> _creditCards;
    private readonly IRepository<Transaction, Guid> _tx;

    public BankingAppService(
        IRepository<Customer, Guid> customers,
        IRepository<Account, Guid> accounts,
        IRepository<DebitCard, Guid> debitCards,
        IRepository<CreditCard, Guid> creditCards,
        IRepository<Transaction, Guid> tx)
    {
        _customers = customers;
        _accounts = accounts;
        _debitCards = debitCards;
        _creditCards = creditCards;
        _tx = tx;
    }
}
