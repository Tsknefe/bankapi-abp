using Microsoft.AspNetCore.Mvc.Testing;

namespace BankApiAbp.HttpApi.Tests.Infrastructure;

public static class TestClientFactory
{
    private static readonly WebApplicationFactory<BankApiAbp.Program> _factory =
        new WebApplicationFactory<BankApiAbp.Program>();

    public static HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }
}