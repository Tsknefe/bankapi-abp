using System.Text.Json;

namespace BankApiAbp.HttpApi.Tests.Infrastructure;

public static class TestAuthHelper
{
    public static async Task<string> GetTokenAsync(
        HttpClient client,
        string username,
        string password)
    {
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "BankApiAbp_Swagger",
            ["scope"] = "BankApiAbp",
            ["username"] = username,
            ["password"] = password
        };

        var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(payload)
        );

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(body)
            .RootElement
            .GetProperty("access_token")
            .GetString()!;
    }
}