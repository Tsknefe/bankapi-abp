namespace BankApiAbp.Banking;

public static class BankingPermissions
{
    public const string GroupName = "Banking";

    public static class Customers
    {
        public const string Default = GroupName + ".Customers";
        public const string Create = Default + ".Create";
        public const string Read = Default + ".Read";
        public const string List = Default + ".List";
    }

    public static class Accounts
    {
        public const string Default = GroupName + ".Accounts";
        public const string Create = Default + ".Create";
        public const string Read = Default + ".Read";
        public const string List = Default + ".List";
        public const string Deposit = Default + ".Deposit";
        public const string Withdraw = Default + ".Withdraw";
        public const string Statement = Default + ".Statement";
        public const string Summary = Default + ".Summary";
    }

    public static class DebitCards
    {
        public const string Default = GroupName + ".DebitCards";
        public const string Create = Default + ".Create";
        public const string Read = Default + ".Read";
        public const string List = Default + ".List";
        public const string Spend = Default + ".Spend";
        public const string SpendSummary = Default + ".SpendSummary";
    }

    public static class CreditCards
    {
        public const string Default = GroupName + ".CreditCards";
        public const string Create = Default + ".Create";
        public const string Read = Default + ".Read";
        public const string List = Default + ".List";
        public const string Spend = Default + ".Spend";
        public const string Pay = Default + ".Pay";
        public const string SpendSummary = Default + ".SpendSummary";
    }

    public static class Transactions
    {
        public const string Default = GroupName + ".Transactions";
        public const string List = Default + ".List";
        public const string Read = Default + ".Read";
    }

    public static class Dashboard
    {
        public const string Default = GroupName + ".Dashboard";
        public const string Summary = Default + ".Summary";
    }
}
