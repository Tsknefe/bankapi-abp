using System.Net.Http.Json;
using System.Text.Json;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Accounts;

public class MyAccountsCacheTests
{
    [Fact]
    public async Task GetMyAccounts_Should_Return_New_Account_After_CreateAccount()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        var before = await GetMyAccounts(client);
        var beforeTotal = GetTotalCount(before);
        var customerId = GetFirstCustomerId(before);

        var iban = BuildUniqueIban();

        var createPayload = new
        {
            customerId,
            name = "Cache Test Account",
            iban,
            accountType = 1,
            initialBalance = 250m
        };

        var createResponse = await client.PostAsJsonAsync("/api/app/banking/account", createPayload);
        var createBody = await createResponse.Content.ReadAsStringAsync();

        createResponse.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)createResponse.StatusCode}, Body={createBody}");

        var after = await GetMyAccounts(client);
        var afterTotal = GetTotalCount(after);

        afterTotal.Should().Be(beforeTotal + 1);
        AccountsShouldContainIban(after, iban).Should().BeTrue();
    }

    private static async Task<string> GetMyAccounts(HttpClient client)
    {
        var response = await client.GetAsync("/api/app/banking/my-accounts?SkipCount=0&MaxResultCount=100");
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

        throw new Exception("MyAccounts response içinde totalCount alanı bulunamadı.");
    }

    private static Guid GetFirstCustomerId(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("items", out var itemsProp) || itemsProp.GetArrayLength() == 0)
            throw new Exception("MyAccounts response içinde hiç item yok.");

        var first = itemsProp[0];

        if (first.TryGetProperty("customerId", out var customerIdProp))
            return customerIdProp.GetGuid();

        throw new Exception("MyAccounts response içinde customerId alanı bulunamadı.");
    }

    private static bool AccountsShouldContainIban(string json, string iban)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("items", out var itemsProp))
            return false;

        foreach (var item in itemsProp.EnumerateArray())
        {
            if (item.TryGetProperty("iban", out var ibanProp) &&
                string.Equals(ibanProp.GetString(), iban, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildUniqueIban()
    {
        var ticks = DateTime.UtcNow.Ticks.ToString();
        var padded = ticks.PadLeft(24, '0');
        var suffix = padded.Length > 24 ? padded[^24..] : padded;
        return "TR" + suffix;
    }
}