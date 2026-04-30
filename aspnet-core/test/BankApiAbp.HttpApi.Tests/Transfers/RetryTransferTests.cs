using System.Net.Http.Json;
using System.Text.Json;
using BankApiAbp.Banking.Infrastructure;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Transfers;

public class RetryTransferTests
{
    private static readonly Guid AccountA = TestUsers.BasicAccountA;
    private static readonly Guid AccountB = TestUsers.BasicAccountB;

    [Fact]
    public async Task Transfer_Should_Retry_On_Transient_Error_And_Succeed_Only_Once()
    {
        using var scope = TestClientFactory.CreateScope();
        var faultInjection = scope.ServiceProvider.GetRequiredService<TestFaultInjection>();
        faultInjection.Reset();
        faultInjection.SetTransientFailureCount(1);

        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        var beforeA = await GetBalance(client, AccountA);
        var beforeB = await GetBalance(client, AccountB);

        var amount = 2m;

        var payload = new
        {
            fromAccountId = AccountA,
            toAccountId = AccountB,
            amount,
            description = "retry transfer test"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"Retry sonrası success bekleniyordu. Body={body}");

        var afterA = await GetBalance(client, AccountA);
        var afterB = await GetBalance(client, AccountB);

        afterA.Should().Be(beforeA - amount);
        afterB.Should().Be(beforeB + amount);

        faultInjection.Reset();
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