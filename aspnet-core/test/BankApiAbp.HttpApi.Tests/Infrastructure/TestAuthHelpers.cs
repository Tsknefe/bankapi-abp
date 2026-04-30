using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;

namespace BankApiAbp.HttpApi.Tests.Infrastructure;

public static class TestAuthHelpers
{
    public static async Task<string> GetTokenAsync(
        HttpClient client,
        string? username = null,
        string? password = null)
    {
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "BankApiAbp_Swagger",
            ["scope"] = "BankApiAbp",
            ["username"] = username ?? TestUsers.BasicUsername,
            ["password"] = password ?? TestUsers.Password
        };

        var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(payload));

        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode
            .Should()
            .BeTrue($"StatusCode={(int)response.StatusCode}, Body={body}");

        return JsonDocument.Parse(body)
            .RootElement
            .GetProperty("access_token")
            .GetString()
            ?? throw new Exception("access_token bulunamadı.");
    }

    public static async Task AuthorizeAsync(
        HttpClient client,
        string? username = null,
        string? password = null)
    {
        var token = await GetTokenAsync(client, username, password);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }
}