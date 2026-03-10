using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace BankApiAbp.HttpApi.Client.ConsoleTestApp;

public class ScenarioRunner
{
    private readonly HttpClient _http;

    public ScenarioRunner(HttpClient http)
    {
        _http = http;
    }

    public async Task RunAsync(
        string username,
        string password,
        Guid accountA,
        Guid accountB)
    {
        Console.WriteLine("=== LOGIN ===");
        var token = await LoginAsync(username, password);

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        await TestSummaryCache(accountA);
        await TestStatementCache(accountA);
        await TestDepositInvalidation(accountA);
        await TestWithdrawInvalidation(accountA);
        await TestTransferInvalidation(accountA, accountB);
        await TestIdempotencySameKeySamePayload(accountA, accountB);
        await TestIdempotencySameKeyDifferentPayload(accountA, accountB);
        await TestRateLimit(accountA, accountB);
    }

private async Task<string> LoginAsync(string username, string password)
{
    var form = new Dictionary<string, string>
    {
        ["grant_type"] = "password",
        ["client_id"] = "BankApiAbp_Swagger",
        ["scope"] = "BankApiAbp",
        ["username"] = username,
        ["password"] = password
    };

    using var req = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
    {
        Content = new FormUrlEncodedContent(form)
    };

    var res = await _http.SendAsync(req);
    var body = await res.Content.ReadAsStringAsync();

    Console.WriteLine("LOGIN STATUS: " + (int)res.StatusCode);
    Console.WriteLine("LOGIN RESPONSE:");
    Console.WriteLine(body);

    res.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(body);
    return doc.RootElement.GetProperty("access_token").GetString()
           ?? throw new Exception("access_token alınamadı");
}
    private async Task TestSummaryCache(Guid accountId)
    {
        Console.WriteLine("\n=== SUMMARY CACHE TEST ===");

        var sw1 = Stopwatch.StartNew();
        var r1 = await _http.GetAsync($"/api/app/banking/account-summary/{accountId}");
        sw1.Stop();
        var b1 = await r1.Content.ReadAsStringAsync();

        var sw2 = Stopwatch.StartNew();
        var r2 = await _http.GetAsync($"/api/app/banking/account-summary/{accountId}");
        sw2.Stop();
        var b2 = await r2.Content.ReadAsStringAsync();

        Console.WriteLine($"1. çağrı: {r1.StatusCode}, {sw1.ElapsedMilliseconds} ms");
        Console.WriteLine(b1);
        Console.WriteLine($"2. çağrı: {r2.StatusCode}, {sw2.ElapsedMilliseconds} ms");
        Console.WriteLine(b2);
    }

    private async Task TestStatementCache(Guid accountId)
    {
        Console.WriteLine("\n=== STATEMENT CACHE TEST ===");

        var url = $"/api/app/banking/account-statement?accountId={accountId}&skipCount=0&maxResultCount=20";

        var sw1 = Stopwatch.StartNew();
        var r1 = await _http.GetAsync(url);
        sw1.Stop();
        var b1 = await r1.Content.ReadAsStringAsync();

        var sw2 = Stopwatch.StartNew();
        var r2 = await _http.GetAsync(url);
        sw2.Stop();
        var b2 = await r2.Content.ReadAsStringAsync();

        Console.WriteLine($"1. çağrı: {r1.StatusCode}, {sw1.ElapsedMilliseconds} ms");
        Console.WriteLine(b1);
        Console.WriteLine($"2. çağrı: {r2.StatusCode}, {sw2.ElapsedMilliseconds} ms");
        Console.WriteLine(b2);
    }

    private async Task TestDepositInvalidation(Guid accountId)
    {
        Console.WriteLine("\n=== DEPOSIT INVALIDATION TEST ===");

        await _http.GetAsync($"/api/app/banking/account-summary/{accountId}");
        await _http.GetAsync($"/api/app/banking/account-summary/{accountId}");

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/deposit");
        req.Headers.Add("Idempotency-Key", $"dep-{Guid.NewGuid()}");
        req.Content = JsonContent.Create(new
        {
            accountId,
            amount = 5,
            description = "console deposit test"
        });

        var depositRes = await _http.SendAsync(req);
        var depositBody = await depositRes.Content.ReadAsStringAsync();

        Console.WriteLine($"Deposit status: {depositRes.StatusCode}");
        Console.WriteLine(depositBody);

        var r1 = await _http.GetAsync($"/api/app/banking/account-summary/{accountId}");
        var b1 = await r1.Content.ReadAsStringAsync();

        var r2 = await _http.GetAsync($"/api/app/banking/account-summary/{accountId}");
        var b2 = await r2.Content.ReadAsStringAsync();

        Console.WriteLine($"Deposit sonrası summary-1: {r1.StatusCode}");
        Console.WriteLine(b1);
        Console.WriteLine($"Deposit sonrası summary-2: {r2.StatusCode}");
        Console.WriteLine(b2);
    }

    private async Task TestWithdrawInvalidation(Guid accountId)
    {
        Console.WriteLine("\n=== WITHDRAW INVALIDATION TEST ===");

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/withdraw");
        req.Headers.Add("Idempotency-Key", $"wdr-{Guid.NewGuid()}");
        req.Content = JsonContent.Create(new
        {
            accountId,
            amount = 1,
            description = "console withdraw test"
        });

        var withdrawRes = await _http.SendAsync(req);
        var withdrawBody = await withdrawRes.Content.ReadAsStringAsync();

        Console.WriteLine($"Withdraw status: {withdrawRes.StatusCode}");
        Console.WriteLine(withdrawBody);

        var r1 = await _http.GetAsync($"/api/app/banking/account-summary/{accountId}");
        var b1 = await r1.Content.ReadAsStringAsync();

        var r2 = await _http.GetAsync($"/api/app/banking/account-summary/{accountId}");
        var b2 = await r2.Content.ReadAsStringAsync();

        Console.WriteLine($"Withdraw sonrası summary-1: {r1.StatusCode}");
        Console.WriteLine(b1);
        Console.WriteLine($"Withdraw sonrası summary-2: {r2.StatusCode}");
        Console.WriteLine(b2);
    }

    private async Task TestTransferInvalidation(Guid fromAccountId, Guid toAccountId)
    {
        Console.WriteLine("\n=== TRANSFER INVALIDATION TEST ===");

        await _http.GetAsync($"/api/app/banking/account-summary/{fromAccountId}");
        await _http.GetAsync($"/api/app/banking/account-summary/{toAccountId}");

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
        req.Headers.Add("Idempotency-Key", $"tr-{Guid.NewGuid()}");
        req.Content = JsonContent.Create(new
        {
            fromAccountId,
            toAccountId,
            amount = 1,
            description = "console transfer test"
        });

        var transferRes = await _http.SendAsync(req);
        var transferBody = await transferRes.Content.ReadAsStringAsync();

        Console.WriteLine($"Transfer status: {transferRes.StatusCode}");
        Console.WriteLine(transferBody);

        var a1 = await _http.GetAsync($"/api/app/banking/account-summary/{fromAccountId}");
        var ab1 = await a1.Content.ReadAsStringAsync();

        var b1 = await _http.GetAsync($"/api/app/banking/account-summary/{toAccountId}");
        var bb1 = await b1.Content.ReadAsStringAsync();

        var a2 = await _http.GetAsync($"/api/app/banking/account-summary/{fromAccountId}");
        var ab2 = await a2.Content.ReadAsStringAsync();

        var b2 = await _http.GetAsync($"/api/app/banking/account-summary/{toAccountId}");
        var bb2 = await b2.Content.ReadAsStringAsync();

        Console.WriteLine($"From summary-1: {a1.StatusCode}");
        Console.WriteLine(ab1);
        Console.WriteLine($"To summary-1: {b1.StatusCode}");
        Console.WriteLine(bb1);
        Console.WriteLine($"From summary-2: {a2.StatusCode}");
        Console.WriteLine(ab2);
        Console.WriteLine($"To summary-2: {b2.StatusCode}");
        Console.WriteLine(bb2);
    }

    private async Task TestIdempotencySameKeySamePayload(Guid fromAccountId, Guid toAccountId)
    {
        Console.WriteLine("\n=== IDEMPOTENCY SAME KEY SAME PAYLOAD ===");

        var idem = $"same-{Guid.NewGuid()}";

        async Task<HttpResponseMessage> Send()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
            req.Headers.Add("Idempotency-Key", idem);
            req.Content = JsonContent.Create(new
            {
                fromAccountId,
                toAccountId,
                amount = 1,
                description = "same key same payload"
            });

            return await _http.SendAsync(req);
        }

        var r1 = await Send();
        var b1 = await r1.Content.ReadAsStringAsync();

        var r2 = await Send();
        var b2 = await r2.Content.ReadAsStringAsync();

        Console.WriteLine($"1. status: {r1.StatusCode}");
        Console.WriteLine(b1);
        Console.WriteLine($"2. status: {r2.StatusCode}");
        Console.WriteLine(b2);
    }

    private async Task TestIdempotencySameKeyDifferentPayload(Guid fromAccountId, Guid toAccountId)
    {
        Console.WriteLine("\n=== IDEMPOTENCY SAME KEY DIFFERENT PAYLOAD ===");

        var idem = $"diff-{Guid.NewGuid()}";

        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
        req1.Headers.Add("Idempotency-Key", idem);
        req1.Content = JsonContent.Create(new
        {
            fromAccountId,
            toAccountId,
            amount = 1,
            description = "same key body1"
        });

        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
        req2.Headers.Add("Idempotency-Key", idem);
        req2.Content = JsonContent.Create(new
        {
            fromAccountId,
            toAccountId,
            amount = 2,
            description = "same key body2"
        });

        var r1 = await _http.SendAsync(req1);
        var b1 = await r1.Content.ReadAsStringAsync();

        var r2 = await _http.SendAsync(req2);
        var b2 = await r2.Content.ReadAsStringAsync();

        Console.WriteLine($"1. status: {r1.StatusCode}");
        Console.WriteLine(b1);
        Console.WriteLine($"2. status: {r2.StatusCode}");
        Console.WriteLine(b2);
    }

    private async Task TestRateLimit(Guid fromAccountId, Guid toAccountId)
    {
        Console.WriteLine("\n=== RATE LIMIT TEST ===");

        for (var i = 0; i < 15; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/app/banking/transfer");
            req.Headers.Add("Idempotency-Key", $"rl-{Guid.NewGuid()}");
            req.Content = JsonContent.Create(new
            {
                fromAccountId,
                toAccountId,
                amount = 1,
                description = $"rate limit test {i}"
            });

            var res = await _http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            Console.WriteLine($"transfer-{i + 1}: {res.StatusCode}");
            Console.WriteLine(body);
        }
    }
}