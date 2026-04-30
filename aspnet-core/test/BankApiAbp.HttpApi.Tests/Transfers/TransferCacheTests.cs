using System.Net.Http.Json;
using System.Text.Json;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Transfers;

public class TransferCacheTests
{
    private static readonly Guid AccountA = TestUsers.BasicAccountA;
    private static readonly Guid AccountB = TestUsers.BasicAccountB;

    [Fact]
    public async Task Transfer_Should_Invalidate_Cache_And_Return_Fresh_Balances()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        var warmA1 = await GetBalance(client, AccountA);
        var warmB1 = await GetBalance(client, AccountB);

        var warmA2 = await GetBalance(client, AccountA);
        var warmB2 = await GetBalance(client, AccountB);

        warmA2.Should().Be(warmA1);
        warmB2.Should().Be(warmB1);

        var amount = 4m;

        var payload = new
        {
            fromAccountId = AccountA,
            toAccountId = AccountB,
            amount,
            description = "transfer cache test " + Guid.NewGuid().ToString("N")
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        var freshA1 = await GetBalance(client, AccountA);
        var freshB1 = await GetBalance(client, AccountB);

        freshA1.Should().Be(warmA1 - amount);
        freshB1.Should().Be(warmB1 + amount);

        var freshA2 = await GetBalance(client, AccountA);
        var freshB2 = await GetBalance(client, AccountB);

        freshA2.Should().Be(freshA1);
        freshB2.Should().Be(freshB1);
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

        throw new Exception("Balance bulunamadı.");
    }
}