using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Accounts;

public class AccountSummaryTests
{
    private static readonly Guid AccountA = TestUsers.BasicAccountA;

    [Fact]
    public async Task Summary_Should_Return_200()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        var response = await client.GetAsync($"/api/app/banking/account-summary/{AccountA}");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");
    }
}