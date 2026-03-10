using BankApiAbp.HttpApi.Client.ConsoleTestApp;
using System.Net.Http;

var baseUrl = "https://localhost:44389";

var accountA = Guid.Parse("3a1f9cad-8add-0dd1-3772-511a6d1f7204");
var accountB = Guid.Parse("3a1fb18d-4621-d1a4-d3e5-a2062ace7fa9");

var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};

using var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri(baseUrl)
};

var runner = new ScenarioRunner(httpClient);

await runner.RunAsync(
    username: "efe",
    password: "Qwe123!",
    accountA: accountA,
    accountB: accountB
);