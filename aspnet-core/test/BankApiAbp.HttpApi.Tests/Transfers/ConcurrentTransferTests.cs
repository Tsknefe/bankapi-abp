using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
namespace BankApiAbp.HttpApi.Tests.Transfers;

public class ConcurrentTransferTests
{
    private static readonly Guid AccountA = TestUsers.ConcurrentAccountA;
    private static readonly Guid AccountB = TestUsers.ConcurrentAccountB;

    [Fact]
    public async Task Concurrent_Transfers_Should_Not_Break_Balance()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.ConcurrentUsername,
            TestUsers.Password);

        var tasks = new List<Task<HttpResponseMessage>>();

        for (var i = 0; i < 10; i++)
        {
            var payload = new
            {
                fromAccountId = AccountA,
                toAccountId = AccountB,
                amount = 1m,
                description = $"concurrent transfer {i}"
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            req.Content = JsonContent.Create(payload);

            tasks.Add(client.SendAsync(req));
        }

        var responses = await Task.WhenAll(tasks);

        foreach (var res in responses)
        {
            var body = await res.Content.ReadAsStringAsync();

            (res.StatusCode == HttpStatusCode.OK ||
             res.StatusCode == HttpStatusCode.Conflict)
                .Should()
                .BeTrue($"StatusCode={(int)res.StatusCode}, Body={body}");
        }
    }
    [Fact]
    public async Task Parallel_Transfers_From_Same_Account_Should_Preserve_Exact_Final_Balance()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.ConcurrentUsername,
            TestUsers.Password);

        var beforeA = await GetBalance(client, AccountA);
        var beforeB = await GetBalance(client, AccountB);

        var transferCount = 5;
        var amountPerTransfer = 1m;

        var tasks = Enumerable.Range(0, transferCount)
            .Select(async i =>
            {
                var payload = new
                {
                    fromAccountId = AccountA,
                    toAccountId = AccountB,
                    amount = amountPerTransfer,
                    description = $"parallel transfer exact balance test {i}"
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                request.Content = JsonContent.Create(payload);

                return await client.SendAsync(request);
            });

        var responses = await Task.WhenAll(tasks);

        foreach (var res in responses)
        {
            var body = await res.Content.ReadAsStringAsync();

            (res.StatusCode == HttpStatusCode.OK ||
             res.StatusCode == HttpStatusCode.Conflict)
                .Should()
                .BeTrue($"StatusCode={(int)res.StatusCode}, Body={body}");
        }

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);

        var afterA = await GetBalance(client, AccountA);
        var afterB = await GetBalance(client, AccountB);

        afterA.Should().Be(beforeA - successCount * amountPerTransfer);
        afterB.Should().Be(beforeB + successCount * amountPerTransfer);
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