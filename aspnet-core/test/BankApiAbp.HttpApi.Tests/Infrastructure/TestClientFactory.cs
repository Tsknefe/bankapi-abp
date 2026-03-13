using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;

namespace BankApiAbp.HttpApi.Tests.Infrastructure;

public static class TestClientFactory
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    private static readonly WebApplicationFactory<BankApiAbp.Program> Factory =
        new WebApplicationFactory<BankApiAbp.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");

                builder.ConfigureServices(services =>
                {
                    services.AddTransient<TestDataSeeder>();
                });
            });

    public static HttpClient CreateClient()
    {
        EnsureSeededAsync().GetAwaiter().GetResult();

        return Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    private static async Task EnsureSeededAsync()
    {
        if (_seeded)
            return;

        await SeedLock.WaitAsync();
        try
        {
            if (_seeded)
                return;

            using var scope = Factory.Services.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<TestDataSeeder>();
            await seeder.SeedAsync();

            _seeded = true;
        }
        finally
        {
            SeedLock.Release();
        }
    }
}