using BankApiAbp.Banking;
using BankApiAbp.Banking.Messaging;
using BankApiAbp.Cards;
using BankApiAbp.Entities;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace BankApiAbp.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class BankApiAbpDbContext :
    AbpDbContext<BankApiAbpDbContext>,
    IIdentityDbContext,
    ITenantManagementDbContext,
    IHasEventInbox,
    IHasEventOutbox
{
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }

    public DbSet<Customer> Customers { get; set; }
    public DbSet<Account> Accounts { get; set; }
    public DbSet<DebitCard> DebitCards { get; set; }
    public DbSet<CreditCard> CreditCards { get; set; }
    public DbSet<BankApiAbp.Transactions.Transaction> Transactions { get; set; }

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }
    public DbSet<BankingIdempotencyRecord> BankingIdempotencyRecords { get; set; }
    public DbSet<LedgerEntry> LedgerEntries { get; set; }

    public DbSet<IncomingEventRecord> IncomingEvents { get; set; }
    public DbSet<OutgoingEventRecord> OutgoingEvents { get; set; }

    public DbSet<InboxMessage> InboxMessages { get; set; }

    public BankApiAbpDbContext(DbContextOptions<BankApiAbpDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureFeatureManagement();
        builder.ConfigureTenantManagement();

        builder.ConfigureEventInbox();
        builder.ConfigureEventOutbox();

        builder.ConfigureBankApiAbp();
        builder.ConfigureBanking();

        builder.Entity<LedgerEntry>(b =>
        {
            b.ToTable("AppLedgerEntries");

            b.ConfigureByConvention();

            b.Property(x => x.Description).HasMaxLength(256);
            b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            b.Property(x => x.BalanceAfter).HasColumnType("numeric(18,2)");
        });
    }
}