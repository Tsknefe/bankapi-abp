using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;

using System.Threading.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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

            builder.Host
                .AddAppSettingsSecretsJson()
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
            var redisConfiguration = builder.Configuration["Redis:Configuration"];

            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConfiguration;
                options.InstanceName = "BankApi:";
            });

            var healthChecks = builder.Services.AddHealthChecks();

            if (!string.IsNullOrWhiteSpace(redisConfiguration))
            {
                healthChecks.AddRedis(redisConfiguration, name: "redis");
            }

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

            /*app.MapGet("/ping", () => Results.Ok(new { message = "pong" }));

            app.MapHealthChecks("/health", new HealthCheckOptions
            {
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "application/json";

                    var payload = new
                    {
                        status = report.Status.ToString(),
                        entries = report.Entries.Select(x => new
                        {
                            name = x.Key,
                            status = x.Value.Status.ToString(),
                            exception = x.Value.Exception?.Message
                        })
                    };

                    await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                }
            });*/
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.Equals("/ping", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("""{"message":"pong"}""");
                    return;
                }

                if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var healthCheckService = context.RequestServices.GetRequiredService<HealthCheckService>();
                        var report = await healthCheckService.CheckHealthAsync();

                        context.Response.ContentType = "application/json";

                        var payload = new
                        {
                            status = report.Status.ToString(),
                            totalDuration = report.TotalDuration.ToString(),
                            entries = report.Entries.Select(x => new
                            {
                                name = x.Key,
                                status = x.Value.Status.ToString(),
                                description = x.Value.Description,
                                duration = x.Value.Duration.ToString(),
                                exception = x.Value.Exception?.Message
                            })
                        };

                        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                        return;
                    }
                    catch (Exception ex)
                    {
                        context.Response.StatusCode = 500;
                        context.Response.ContentType = "application/json";

                        var errorPayload = new
                        {
                            status = "Error",
                            message = ex.Message,
                            detail = ex.ToString()
                        };

                        await context.Response.WriteAsync(JsonSerializer.Serialize(errorPayload));
                        return;
                    }
                }

                await next();
            });

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