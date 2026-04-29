using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Transfers;

public class RiskEngineTests
{
    private readonly HttpClient _client;

    public RiskEngineTests()
    {
        _client = TestClientFactory.CreateClient();
    }

    [Fact]
    public async Task Should_Block_When_Velocity_And_High_Amount()
    {
        await AuthorizeAsync();

        var customerId = await CreateCustomerAsync();
        var fromAccountId = await CreateAccountAsync(customerId, "Risk Source", 100000m);
        var toAccountId = await CreateAccountAsync(customerId, "Risk Target", 0m);

        for (var i = 0; i < 3; i++)
        {
            var response = await PostTransferAsync(
                fromAccountId,
                toAccountId,
                1m,
                $"velocity risk seed {i + 1}");

            response.EnsureSuccessStatusCode();
        }

        var blockedResponse = await PostTransferAsync(
            fromAccountId,
            toAccountId,
            50000m,
            "risk score block test");

        Assert.Equal(HttpStatusCode.Forbidden, blockedResponse.StatusCode);

        var body = await blockedResponse.Content.ReadAsStringAsync();

        Assert.Contains("RISK:SCORE_BLOCKED", body);
    }

    [Fact]
    public async Task Should_Flag_But_Allow_When_Only_High_Amount()
    {
        await AuthorizeAsync();

        var customerId = await CreateCustomerAsync();
        var fromAccountId = await CreateAccountAsync(customerId, "Risk Flag Source", 100000m);
        var toAccountId = await CreateAccountAsync(customerId, "Risk Flag Target", 0m);

        var response = await PostTransferAsync(
            fromAccountId,
            toAccountId,
            50000m,
            "risk score flag test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"amount\":50000", body);
    }

    [Fact]
    public async Task Should_Block_When_Self_Transfer()
    {
        await AuthorizeAsync();

        var customerId = await CreateCustomerAsync();
        var accountId = await CreateAccountAsync(customerId, "Risk Self Source", 100000m);

        var response = await PostTransferAsync(
            accountId,
            accountId,
            10m,
            "risk self transfer test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("RISK:SELF_TRANSFER_BLOCKED", body);
    }

    private async Task AuthorizeAsync()
    {
        var token = await TestAuthHelpers.GetTokenAsync(_client);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<Guid> CreateCustomerAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/app/banking/customer", new
        {
            name = $"Risk Test Customer {Guid.NewGuid():N}",
            tcNo = Random.Shared.NextInt64(10000000000, 99999999999).ToString(),
            birthDate = "1995-01-01",
            birthPlace = "Eskisehir"
        });

        response.EnsureSuccessStatusCode();

        return await ReadIdAsync(response);
    }
    private async Task<Guid> CreateAccountAsync(Guid customerId, string name, decimal initialBalance)
    {
        var response = await _client.PostAsJsonAsync("/api/app/banking/account", new
        {
            customerId,
            name = $"{name} {Guid.NewGuid():N}",
            iban = $"TR{Random.Shared.NextInt64(100000000000000000, 999999999999999999)}",
            accountType = 1,
            initialBalance
        });

        response.EnsureSuccessStatusCode();

        return await ReadIdAsync(response);
    }

    private async Task<HttpResponseMessage> PostTransferAsync(
        Guid fromAccountId,
        Guid toAccountId,
        decimal amount,
        string description)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/app/banking/transfer");

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        request.Content = JsonContent.Create(new
        {
            fromAccountId,
            toAccountId,
            amount,
            description
        });

        return await _client.SendAsync(request);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.GetProperty("id").GetGuid();
    }
}