using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

using Serilog.Sinks.Elasticsearch;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace BankApiAbp;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        try
        {
            Log.Information("Starting BankApiAbp.HttpApi.Host.");

            var builder = WebApplication.CreateBuilder(args);

            var elasticUri = builder.Configuration["Elastic:Uri"];
            var elasticIndexFormat = builder.Configuration["Elastic:IndexFormat"] ?? "bankapiabp-logs-{0:yyyy.MM.dd}";

            builder.Host.AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog((ctx, services, lc) =>
                {
                    lc.ReadFrom.Configuration(ctx.Configuration)
                      .ReadFrom.Services(services)
                      .Enrich.FromLogContext()
                      .WriteTo.Async(c => c.File("Logs/logs.txt"))
                      .WriteTo.Async(c => c.Console());

                    if (!string.IsNullOrWhiteSpace(elasticUri))
                    {
                        lc.WriteTo.Async(c => c.Elasticsearch(new ElasticsearchSinkOptions(new Uri(elasticUri))
                        {
                            IndexFormat = elasticIndexFormat,
                            AutoRegisterTemplate = true,
                            MinimumLogEventLevel = LogEventLevel.Information
                        }));
                    }
                });

            var globalPermit = builder.Configuration.GetValue("RateLimiting:Global:PermitLimit", 120);
            var globalWindowSeconds = builder.Configuration.GetValue("RateLimiting:Global:WindowSeconds", 60);

            var transferTokenLimit = builder.Configuration.GetValue("RateLimiting:TransferPolicy:TokenLimit", 10);
            var transferTokensPerPeriod = builder.Configuration.GetValue("RateLimiting:TransferPolicy:TokensPerPeriod", 10);
            var transferPeriodSeconds = builder.Configuration.GetValue("RateLimiting:TransferPolicy:PeriodSeconds", 60);

            builder.Services.AddRateLimiter(options =>
            {
                if (builder.Environment.IsEnvironment("Test"))
                {
                    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                        RateLimitPartition.GetNoLimiter("test-global"));

                    options.AddPolicy("transfer", _ =>
                        RateLimitPartition.GetNoLimiter("test-transfer"));

                    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                    return;
                }

                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    return RateLimitPartition.GetFixedWindowLimiter(ip, _ =>
                        new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = globalPermit,
                            Window = TimeSpan.FromSeconds(globalWindowSeconds),
                            QueueLimit = 0
                        });
                });

                options.AddPolicy("transfer", httpContext =>
                {
                    string? userId =
                        httpContext.User?.FindFirst("sub")?.Value
                        ?? httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                    var key = !string.IsNullOrWhiteSpace(userId)
                        ? $"user:{userId}"
                        : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon"}";

                    return RateLimitPartition.GetTokenBucketLimiter(key, _ =>
                        new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = transferTokenLimit,
                            TokensPerPeriod = transferTokensPerPeriod,
                            ReplenishmentPeriod = TimeSpan.FromSeconds(transferPeriodSeconds),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                });

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });
            var otelServiceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "BankApiAbp.HttpApi.Host";
            var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = builder.Configuration["Redis:Configuration"];
                options.InstanceName = "BankApi:";
            });

            builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(otelServiceName))
                .WithTracing(t =>
                {
                    t.AddAspNetCoreInstrumentation()
                     .AddHttpClientInstrumentation()
                     .AddEntityFrameworkCoreInstrumentation();

                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                    }
                })
                .WithMetrics(m =>
                {
                    m.AddAspNetCoreInstrumentation()
                     .AddRuntimeInstrumentation()
                     .AddProcessInstrumentation()
                     .AddMeter("BankApiAbp.Banking");

                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                    }
                });

            await builder.AddApplicationAsync<BankApiAbpHttpApiHostModule>();
            var app = builder.Build();

            await app.InitializeApplicationAsync();

            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
                throw;

            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}