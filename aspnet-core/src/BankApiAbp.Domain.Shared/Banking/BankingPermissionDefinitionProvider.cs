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

        var customers = GetOrAddPermission(group, BankingPermissions.Customers.Default, L("Permission:Customers"));
        AddChildIfNotExists(group, customers, BankingPermissions.Customers.Create, L("Permission:Create"));
        AddChildIfNotExists(group, customers, BankingPermissions.Customers.Read, L("Permission:Read"));
        AddChildIfNotExists(group, customers, BankingPermissions.Customers.List, L("Permission:List"));

        var accounts = GetOrAddPermission(group, BankingPermissions.Accounts.Default, L("Permission:Accounts"));
        AddChildIfNotExists(group, accounts, BankingPermissions.Accounts.Create, L("Permission:Create"));
        AddChildIfNotExists(group, accounts, BankingPermissions.Accounts.Read, L("Permission:Read"));
        AddChildIfNotExists(group, accounts, BankingPermissions.Accounts.List, L("Permission:List"));
        AddChildIfNotExists(group, accounts, BankingPermissions.Accounts.Deposit, L("Permission:Deposit"));
        AddChildIfNotExists(group, accounts, BankingPermissions.Accounts.Transfer, L("Permission:Transfer"));
        AddChildIfNotExists(group, accounts, BankingPermissions.Accounts.Withdraw, L("Permission:Withdraw"));
        AddChildIfNotExists(group, accounts, BankingPermissions.Accounts.Statement, L("Permission:Statement"));
        AddChildIfNotExists(group, accounts, BankingPermissions.Accounts.Summary, L("Permission:Summary"));

        var debit = GetOrAddPermission(group, BankingPermissions.DebitCards.Default, L("Permission:DebitCards"));
        AddChildIfNotExists(group, debit, BankingPermissions.DebitCards.Create, L("Permission:Create"));
        AddChildIfNotExists(group, debit, BankingPermissions.DebitCards.Read, L("Permission:Read"));
        AddChildIfNotExists(group, debit, BankingPermissions.DebitCards.List, L("Permission:List"));
        AddChildIfNotExists(group, debit, BankingPermissions.DebitCards.Spend, L("Permission:Spend"));
        AddChildIfNotExists(group, debit, BankingPermissions.DebitCards.SpendSummary, L("Permission:SpendSummary"));

        var credit = GetOrAddPermission(group, BankingPermissions.CreditCards.Default, L("Permission:CreditCards"));
        AddChildIfNotExists(group, credit, BankingPermissions.CreditCards.Create, L("Permission:Create"));
        AddChildIfNotExists(group, credit, BankingPermissions.CreditCards.Read, L("Permission:Read"));
        AddChildIfNotExists(group, credit, BankingPermissions.CreditCards.List, L("Permission:List"));
        AddChildIfNotExists(group, credit, BankingPermissions.CreditCards.Spend, L("Permission:Spend"));
        AddChildIfNotExists(group, credit, BankingPermissions.CreditCards.Pay, L("Permission:Pay"));
        AddChildIfNotExists(group, credit, BankingPermissions.CreditCards.SpendSummary, L("Permission:SpendSummary"));

        var tx = GetOrAddPermission(group, BankingPermissions.Transactions.Default, L("Permission:Transactions"));
        AddChildIfNotExists(group, tx, BankingPermissions.Transactions.List, L("Permission:List"));
        AddChildIfNotExists(group, tx, BankingPermissions.Transactions.Read, L("Permission:Read"));

        var dash = GetOrAddPermission(group, BankingPermissions.Dashboard.Default, L("Permission:Dashboard"));
        AddChildIfNotExists(group, dash, BankingPermissions.Dashboard.Summary, L("Permission:Summary"));
    }

    private static PermissionDefinition GetOrAddPermission(
        PermissionGroupDefinition group,
        string name,
        LocalizableString displayName)
        => group.GetPermissionOrNull(name) ?? group.AddPermission(name, displayName);

    private static void AddChildIfNotExists(
        PermissionGroupDefinition group,
        PermissionDefinition parent,
        string name,
        LocalizableString displayName)
    {
        if (group.GetPermissionOrNull(name) != null)
            return;

        parent.AddChild(name, displayName);
    }

    private static LocalizableString L(string name)
        => LocalizableString.Create<BankApiAbpResource>(name);
}