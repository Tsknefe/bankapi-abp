using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.Transfers;

public class ConcurrentTransferTests
{
    private static readonly Guid AccountA =
        Guid.Parse("3a1f9cad-8add-0dd1-3772-511a6d1f7204");

    private static readonly Guid AccountB =
        Guid.Parse("3a1fb18d-4621-d1a4-d3e5-a2062ace7fa9");

    [Fact]
    public async Task Concurrent_Transfers_Should_Not_Break_Balance()
    {
        using var client = TestClientFactory.CreateClient();

        var token = await GetToken(client);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var tasks = new List<Task<HttpResponseMessage>>();

        for (var i = 0; i < 10; i++)
        {
            var payload = new
            {
                fromAccountId = AccountA,
                toAccountId = AccountB,
                amount = 1,
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

            res.IsSuccessStatusCode
                .Should()
                .BeTrue($"StatusCode={(int)res.StatusCode}, Body={body}");
        }
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

        var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(payload)
        );

        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        var token = JsonDocument.Parse(body)
            .RootElement
            .GetProperty("access_token")
            .GetString();

        return token!;
    }
}