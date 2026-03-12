using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BankApiAbp.HttpApi.Tests.Infrastructure;

public static class TestAuthHelpers
{
    public static async Task<string> GetTokenAsync(
        HttpClient client,
        string userName = "admin",
        string password = "1q2w3E*")
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = userName,
            ["password"] = password,
            ["client_id"] = "BankApiAbp_Swagger",
            ["scope"] = "openid profile email roles BankApiAbp"
        });

        var response = await client.PostAsync("/connect/token", form);
        var body = await response.Content.ReadAsStringAsync();

        Console.WriteLine("TOKEN STATUS: " + (int)response.StatusCode);
        Console.WriteLine("TOKEN BODY:");
        Console.WriteLine(body);

        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (token is null || string.IsNullOrWhiteSpace(token.access_token))
        {
            throw new InvalidOperationException("Access token alınamadı.");
        }

        return token.access_token;
    }

    public static async Task AuthorizeAsync(
        HttpClient client,
        string userName = "admin",
        string password = "1q2w3E*")
    {
        var token = await GetTokenAsync(client, userName, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private sealed class TokenResponse
    {
        public string access_token { get; set; } = string.Empty;
        public string token_type { get; set; } = string.Empty;
        public int expires_in { get; set; }
    }
}