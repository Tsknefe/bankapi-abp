using System.Net;
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
        var beforeB = await GetBalance(client, AccountB);

        var key = Guid.NewGuid().ToString();
        var amount = 1m;

        var payload = new
        {
            fromAccountId = AccountA,
            toAccountId = AccountB,
            amount,
            description = "idempotency same payload test"
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

        (r1.StatusCode == HttpStatusCode.OK || r1.StatusCode == HttpStatusCode.Conflict)
            .Should()
            .BeTrue($"First response unexpected. StatusCode={(int)r1.StatusCode}, Body={b1}");

        (r2.StatusCode == HttpStatusCode.OK || r2.StatusCode == HttpStatusCode.Conflict)
            .Should()
            .BeTrue($"Second response unexpected. StatusCode={(int)r2.StatusCode}, Body={b2}");

        var afterA = await GetBalance(client, AccountA);
        var afterB = await GetBalance(client, AccountB);

        var diffA = beforeA - afterA;
        var diffB = afterB - beforeB;

        diffA.Should().Be(amount, "same idempotency key should debit source only once");
        diffB.Should().Be(amount, "same idempotency key should credit target only once");
    }

    [Fact]
    public async Task Same_Idempotency_Key_With_Different_Payload_Should_Be_Rejected()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.BasicUsername,
            TestUsers.Password);

        var beforeA = await GetBalance(client, AccountA);
        var beforeB = await GetBalance(client, AccountB);

        var key = Guid.NewGuid().ToString();

        var payload1 = new
        {
            fromAccountId = AccountA,
            toAccountId = AccountB,
            amount = 1m,
            description = "idempotency first payload"
        };

        var payload2 = new
        {
            fromAccountId = AccountA,
            toAccountId = AccountB,
            amount = 2m,
            description = "idempotency second payload"
        };

        async Task<HttpResponseMessage> SendAsync(object payload)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
            req.Headers.Add("Idempotency-Key", key);
            req.Content = JsonContent.Create(payload);
            return await client.SendAsync(req);
        }

        var r1 = await SendAsync(payload1);
        var b1 = await r1.Content.ReadAsStringAsync();

        var r2 = await SendAsync(payload2);
        var b2 = await r2.Content.ReadAsStringAsync();

        r1.IsSuccessStatusCode.Should().BeTrue($"First request failed. Body={b1}");
        r2.IsSuccessStatusCode.Should().BeFalse($"Second request should be rejected. Body={b2}");

        var afterA = await GetBalance(client, AccountA);
        var afterB = await GetBalance(client, AccountB);

        (beforeA - afterA).Should().Be(1m, "first request should be applied exactly once");
        (afterB - beforeB).Should().Be(1m, "second request with same key but different payload must not be applied");
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