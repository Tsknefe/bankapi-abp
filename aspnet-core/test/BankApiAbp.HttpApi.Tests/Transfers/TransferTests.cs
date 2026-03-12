using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Transfers;

public class TransferTests
{
    private static readonly Guid AccountA =
        Guid.Parse("3a1f9cad-8add-0dd1-3772-511a6d1f7204");

    private static readonly Guid AccountB =
        Guid.Parse("3a1fb18d-4621-d1a4-d3e5-a2062ace7fa9");

    [Fact]
    public async Task Transfer_Should_Move_Money_From_A_To_B()
    {
        using var client = TestClientFactory.CreateClient();

        var token = await GetToken(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var beforeA = await GetBalance(client, AccountA);
        var beforeB = await GetBalance(client, AccountB);

        var amount = 1m;

        var payload = new
        {
            fromAccountId = AccountA,
            toAccountId = AccountB,
            amount,
            description = "test transfer"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        var afterA = await GetBalance(client, AccountA);
        var afterB = await GetBalance(client, AccountB);

        afterA.Should().BeLessThan(beforeA);
        afterB.Should().BeGreaterThan(beforeB);
    }

    private static async Task<decimal> GetBalance(HttpClient client, Guid accountId)
    {
        var response = await client.GetAsync($"/api/app/banking/account-summary/{accountId}");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        using var doc = JsonDocument.Parse(body);

        if (doc.RootElement.TryGetProperty("balance", out var balanceProp))
            return balanceProp.GetDecimal();

        if (doc.RootElement.TryGetProperty("currentBalance", out var currentBalanceProp))
            return currentBalanceProp.GetDecimal();

        throw new Exception("Summary response içinde balance/currentBalance alanı bulunamadı.");
    }

    private static async Task<string> GetToken(HttpClient client)
    {
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "BankApiAbp_Swagger",
            ["scope"] = "BankApiAbp",
            ["username"] = "efe",
            ["password"] = "Qwe123!"
        };

        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(payload));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        return JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()
               ?? throw new Exception("access_token bulunamadı");
    }
}