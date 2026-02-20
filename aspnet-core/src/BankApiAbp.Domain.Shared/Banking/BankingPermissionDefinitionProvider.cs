using BankApiAbp.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace BankApiAbp.Banking;

public class BankingPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetGroupOrNull(BankingPermissions.GroupName)
        ?? context.AddGroup(BankingPermissions.GroupName, L("Permission:Banking"));

        var customers = group.AddPermission(BankingPermissions.Customers.Default, L("Permission:Customers"));
        customers.AddChild(BankingPermissions.Customers.Create, L("Permission:Create"));
        customers.AddChild(BankingPermissions.Customers.Read, L("Permission:Read"));
        customers.AddChild(BankingPermissions.Customers.List, L("Permission:List"));

        var accounts = group.AddPermission(BankingPermissions.Accounts.Default, L("Permission:Accounts"));
        accounts.AddChild(BankingPermissions.Accounts.Create, L("Permission:Create"));
        accounts.AddChild(BankingPermissions.Accounts.Read, L("Permission:Read"));
        accounts.AddChild(BankingPermissions.Accounts.List, L("Permission:List"));
        accounts.AddChild(BankingPermissions.Accounts.Deposit, L("Permission:Deposit"));
        accounts.AddChild(BankingPermissions.Accounts.Withdraw, L("Permission:Withdraw"));
        accounts.AddChild(BankingPermissions.Accounts.Statement, L("Permission:Statement"));
        accounts.AddChild(BankingPermissions.Accounts.Summary, L("Permission:Summary"));

        var debit = group.AddPermission(BankingPermissions.DebitCards.Default, L("Permission:DebitCards"));
        debit.AddChild(BankingPermissions.DebitCards.Create, L("Permission:Create"));
        debit.AddChild(BankingPermissions.DebitCards.Read, L("Permission:Read"));
        debit.AddChild(BankingPermissions.DebitCards.List, L("Permission:List"));
        debit.AddChild(BankingPermissions.DebitCards.Spend, L("Permission:Spend"));
        debit.AddChild(BankingPermissions.DebitCards.SpendSummary, L("Permission:SpendSummary"));

        var credit = group.AddPermission(BankingPermissions.CreditCards.Default, L("Permission:CreditCards"));
        credit.AddChild(BankingPermissions.CreditCards.Create, L("Permission:Create"));
        credit.AddChild(BankingPermissions.CreditCards.Read, L("Permission:Read"));
        credit.AddChild(BankingPermissions.CreditCards.List, L("Permission:List"));
        credit.AddChild(BankingPermissions.CreditCards.Spend, L("Permission:Spend"));
        credit.AddChild(BankingPermissions.CreditCards.Pay, L("Permission:Pay"));
        credit.AddChild(BankingPermissions.CreditCards.SpendSummary, L("Permission:SpendSummary"));

        var tx = group.AddPermission(BankingPermissions.Transactions.Default, L("Permission:Transactions"));
        tx.AddChild(BankingPermissions.Transactions.List, L("Permission:List"));
        tx.AddChild(BankingPermissions.Transactions.Read, L("Permission:Read"));

        var dash = group.AddPermission(BankingPermissions.Dashboard.Default, L("Permission:Dashboard"));
        dash.AddChild(BankingPermissions.Dashboard.Summary, L("Permission:Summary"));
    }

    private static LocalizableString L(string name)
        => LocalizableString.Create<BankApiAbpResource>(name);
}
