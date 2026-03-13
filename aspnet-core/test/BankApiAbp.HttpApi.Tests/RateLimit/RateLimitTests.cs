using System.Net;
using System.Net.Http.Json;
using BankApiAbp.HttpApi.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace BankApiAbp.HttpApi.Tests.RateLimit;

[Collection("Rate Limit Tests")]
public class RateLimitTests
{
    private static readonly Guid AccountA = TestUsers.RateLimitAccountA;
    private static readonly Guid AccountB = TestUsers.RateLimitAccountB;

    [Fact]
    public async Task Should_Return_429_When_Rate_Limit_Is_Exceeded()
    {
        using var client = TestClientFactory.CreateClient();

        await TestAuthHelpers.AuthorizeAsync(
            client,
            TestUsers.RateLimitUsername,
            TestUsers.Password);

        HttpStatusCode? lastStatus = null;

        for (var i = 0; i < 15; i++)
        {
            var payload = new
            {
                fromAccountId = AccountA,
                toAccountId = AccountB,
                amount = 1m,
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
}