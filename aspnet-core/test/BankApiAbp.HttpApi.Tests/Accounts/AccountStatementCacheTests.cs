using System.Net.Http.Json;
using System.Text.Json;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Accounts;

public class AccountStatementCacheTests
{
    private static readonly Guid AccountA = TestUsers.BasicAccountA;

    [Fact]
    public async Task Statement_Should_Include_New_Transaction_After_Deposit()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        var before = await GetStatement(client, AccountA);
        var beforeTotal = GetTotalCount(before);

        var description = "statement cache test " + Guid.NewGuid().ToString("N");

        var payload = new
        {
            accountId = AccountA,
            amount = 3m,
            description = description
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/deposit");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        var after = await GetStatement(client, AccountA);
        var afterTotal = GetTotalCount(after);

        afterTotal.Should().BeGreaterThan(beforeTotal);
        StatementShouldContainDescription(after, description).Should().BeTrue();
    }

    private static async Task<string> GetStatement(HttpClient client, Guid accountId)
    {
        var response = await client.GetAsync($"/api/app/banking/account-statement?accountId={accountId}&SkipCount=0&MaxResultCount=20");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        return body;
    }

    private static int GetTotalCount(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("totalCount", out var totalCountProp))
            return totalCountProp.GetInt32();

        throw new Exception("Statement response içinde totalCount bulunamadı.");
    }

    private static bool StatementShouldContainDescription(string json, string description)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("items", out var itemsProp))
            return false;

        foreach (var item in itemsProp.EnumerateArray())
        {
            if (item.TryGetProperty("description", out var descProp) &&
                string.Equals(descProp.GetString(), description, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}