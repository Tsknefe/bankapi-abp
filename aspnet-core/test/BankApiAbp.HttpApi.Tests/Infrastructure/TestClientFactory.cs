using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using Volo.Abp.Uow;

namespace BankApiAbp.HttpApi.Tests.Infrastructure;

public static class TestClientFactory
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    private static readonly WebApplicationFactory<BankApiAbp.Program> TestFactory =
        new WebApplicationFactory<BankApiAbp.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");

                builder.ConfigureServices(services =>
                {
                    services.AddTransient<TestDataSeeder>();
                });
            });

    private static readonly WebApplicationFactory<BankApiAbp.Program> RateLimitFactory =
        new WebApplicationFactory<BankApiAbp.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("RateLimitTest");

                builder.ConfigureServices(services =>
                {
                    services.AddTransient<TestDataSeeder>();
                });
            });

    public static HttpClient CreateClient()
    {
        EnsureSeededAsync(TestFactory).GetAwaiter().GetResult();

        return TestFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    public static HttpClient CreateRateLimitClient()
    {
        EnsureSeededAsync(RateLimitFactory).GetAwaiter().GetResult();

        return RateLimitFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    private static async Task EnsureSeededAsync(WebApplicationFactory<BankApiAbp.Program> factory)
    {
        if (_seeded)
            return;

        await SeedLock.WaitAsync();
        try
        {
            if (_seeded)
                return;

            using var scope = factory.Services.CreateScope();

            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var seeder = scope.ServiceProvider.GetRequiredService<TestDataSeeder>();

            using (var uow = uowManager.Begin(requiresNew: true, isTransactional: false))
            {
                await seeder.SeedAsync();
                await uow.CompleteAsync();
            }

            _seeded = true;
        }
        finally
        {
            SeedLock.Release();
        }
    }
}