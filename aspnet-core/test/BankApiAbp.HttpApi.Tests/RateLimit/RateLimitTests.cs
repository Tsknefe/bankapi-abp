using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.RateLimit;
[Collection("Rate Limit Tests")]
public class RateLimitTests
{
    private static readonly Guid AccountA =
        Guid.Parse("3a1f9cad-8add-0dd1-3772-511a6d1f7204");

    private static readonly Guid AccountB =
        Guid.Parse("3a1fb18d-4621-d1a4-d3e5-a2062ace7fa9");

    [Fact]
    public async Task Should_Return_429_When_Rate_Limit_Is_Exceeded()
    {
        using var client = TestClientFactory.Create();

        var token = await GetToken(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        HttpStatusCode? lastStatus = null;

        for (var i = 0; i < 15; i++)
        {
            var payload = new
            {
                fromAccountId = AccountA,
                toAccountId = AccountB,
                amount = 1,
                description = $"rate limit test {i}"
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            req.Content = JsonContent.Create(payload);

            var response = await client.SendAsync(req);
            lastStatus = response.StatusCode;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        lastStatus.Should().Be(HttpStatusCode.TooManyRequests);
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