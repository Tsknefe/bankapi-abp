using System.Net.Http.Json;
using System.Text.Json;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Transfers;

public class IdempotencyTests
{
    private static readonly Guid AccountA = TestUsers.BasicAccountA;
    private static readonly Guid AccountB = TestUsers.BasicAccountB;

    [Fact]
    public async Task Same_Idempotency_Key_Should_Not_Execute_Twice()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        var beforeA = await GetBalance(client, AccountA);

        var key = Guid.NewGuid().ToString();

        var payload = new
        {
            fromAccountId = AccountA,
            toAccountId = AccountB,
            amount = 1m,
            description = "idempotency test"
        };

        async Task<HttpResponseMessage> SendAsync()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
            req.Headers.Add("Idempotency-Key", key);
            req.Content = JsonContent.Create(payload);
            return await client.SendAsync(req);
        }

        var r1 = await SendAsync();
        var b1 = await r1.Content.ReadAsStringAsync();

        var r2 = await SendAsync();
        var b2 = await r2.Content.ReadAsStringAsync();

        r1.IsSuccessStatusCode.Should().BeTrue($"Body={b1}");
        r2.IsSuccessStatusCode.Should().BeTrue($"Body={b2}");

        var afterA = await GetBalance(client, AccountA);

        var diff = beforeA - afterA;
        diff.Should().BeGreaterThan(0m);
        diff.Should().BeLessThan(2m);
    }

    private static async Task<decimal> GetBalance(HttpClient client, Guid accountId)
    {
        var response = await client.GetAsync($"/api/app/banking/account-summary/{accountId}");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        using var doc = JsonDocument.Parse(body);

        if (doc.RootElement.TryGetProperty("balance", out var balanceProp))
            return balanceProp.GetDecimal();

        if (doc.RootElement.TryGetProperty("currentBalance", out var currentBalanceProp))
            return currentBalanceProp.GetDecimal();

        throw new Exception("Summary response içinde balance/currentBalance alanı bulunamadı.");
    }
}