namespace BankApiAbp.Banking;

public enum TransactionType
{
    Deposit = 0,
    Withdraw = 1,
    DebitCardSpend = 2,
    CreditCardSpend = 3,
    CreditCardPayment = 4
}
