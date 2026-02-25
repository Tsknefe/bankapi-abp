using System;
using BankApiAbp.Entities;
using BankApiAbp.Cards;
using BankApiAbp.Transactions;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using BankApiAbp.Banking.Infrastructure;
using Microsoft.AspNetCore.Http;
using Volo.Abp.DistributedLocking;

namespace BankApiAbp.Banking;

public partial class BankingAppService : ApplicationService, IBankingAppService
{
    private readonly IRepository<Customer, Guid> _customers;
    private readonly IRepository<Account, Guid> _accounts;
    private readonly IRepository<DebitCard, Guid> _debitCards;
    private readonly IRepository<CreditCard, Guid> _creditCards;
    private readonly IRepository<Transaction, Guid> _tx;

    private readonly RetryExecutor _retry;
    private readonly RowLockHelper _rowLock;
    private readonly IdempotencyGate _idem;
    private readonly IHttpContextAccessor _http;
    private readonly IAbpDistributedLock _distributedLock;

    public BankingAppService(
        IRepository<Customer, Guid> customers,
        IRepository<Account, Guid> accounts,
        IRepository<DebitCard, Guid> debitCards,
        IRepository<CreditCard, Guid> creditCards,
        IRepository<Transaction, Guid> tx,
        RetryExecutor retry,
        RowLockHelper rowLock,
        IdempotencyGate idem,
        IHttpContextAccessor http,
        IAbpDistributedLock distributedLock)
    {
        _customers = customers;
        _accounts = accounts;
        _debitCards = debitCards;
        _creditCards = creditCards;
        _tx = tx;

        _retry = retry;
        _rowLock = rowLock;
        _idem = idem;
        _http = http;

        _distributedLock = distributedLock;
    }
}