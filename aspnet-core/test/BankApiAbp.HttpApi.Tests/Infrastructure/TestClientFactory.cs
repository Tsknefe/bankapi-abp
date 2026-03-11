using System.Net.Http;

namespace BankApiAbp.HttpApi.Tests.Infrastructure;

public static class TestClientFactory
{
    public static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost:44389")
        };
    }
}