using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
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
}