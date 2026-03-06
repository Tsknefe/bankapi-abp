using BankApiAbp.HttpApi.Client.ConsoleTestApp;
using System.Net.Http;
using System;

var baseUrl = "https://localhost:44389";

var accountA = Guid.Parse("BURAYA_A_HESAP_GUID");
var accountB = Guid.Parse("BURAYA_B_HESAP_GUID");

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
    username: "admin",
    password: "123qwe",
    accountA: accountA,
    accountB: accountB
);