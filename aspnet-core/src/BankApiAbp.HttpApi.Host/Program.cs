using System;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
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
using Volo.Abp.Autofac;

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

            builder.Host.UseAutofac();
            builder.Host.UseSerilog();

            var elasticUri = builder.Configuration["ElasticConfiguration:Uri"];

            if (!string.IsNullOrWhiteSpace(elasticUri))
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
                    .WriteTo.Async(c => c.Elasticsearch(new ElasticsearchSinkOptions(new Uri(elasticUri))
                    {
                        AutoRegisterTemplate = true,
                        IndexFormat = "bankapiabp-logs-{0:yyyy.MM}"
                    }))
                    .CreateLogger();

                builder.Host.UseSerilog();
            }

            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                if (builder.Environment.IsEnvironment("Test"))
                {
                    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                        RateLimitPartition.GetNoLimiter("test-global"));

                    options.AddPolicy("transfer", _ =>
                        RateLimitPartition.GetNoLimiter("test-transfer"));
                }
                else
                {
                    var globalPermit = builder.Configuration.GetValue<int?>("RateLimiting:Global:PermitLimit") ?? 100;
                    var globalWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Global:WindowSeconds") ?? 60;

                    var transferTokenLimit = builder.Configuration.GetValue<int?>("RateLimiting:Transfer:TokenLimit") ?? 5;
                    var transferTokensPerPeriod = builder.Configuration.GetValue<int?>("RateLimiting:Transfer:TokensPerPeriod") ?? 5;
                    var transferPeriodSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Transfer:PeriodSeconds") ?? 60;

                    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    {
                        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                        return RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: $"global:{ip}",
                            factory: _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = globalPermit,
                                Window = TimeSpan.FromSeconds(globalWindowSeconds),
                                QueueLimit = 0,
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                                AutoReplenishment = true
                            });
                    });

                    options.AddPolicy("transfer", httpContext =>
                    {
                        string? userId =
                            httpContext.User?.FindFirst("sub")?.Value
                            ?? httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                        var key = !string.IsNullOrWhiteSpace(userId)
                            ? $"transfer:user:{userId}"
                            : $"transfer:ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon"}";

                        return RateLimitPartition.GetTokenBucketLimiter(
                            partitionKey: key,
                            factory: _ => new TokenBucketRateLimiterOptions
                            {
                                TokenLimit = transferTokenLimit,
                                TokensPerPeriod = transferTokensPerPeriod,
                                ReplenishmentPeriod = TimeSpan.FromSeconds(transferPeriodSeconds),
                                QueueLimit = 0,
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                                AutoReplenishment = true
                            });
                    });
                }
            });

            if (builder.Environment.IsEnvironment("Test") ||
                builder.Environment.IsEnvironment("RateLimitTest"))
            {
                builder.Services.Configure<StatusCodePagesOptions>(options =>
                {
                    options.HandleAsync = context => Task.CompletedTask;
                });
            }

            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource =>
                {
                    resource.AddService("BankApiAbp.HttpApi.Host");
                })
                .WithTracing(tracing =>
                {
                    tracing
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddEntityFrameworkCoreInstrumentation();

                    var otlpEndpoint = builder.Configuration["OpenTelemetry:Otlp:Endpoint"];
                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        tracing.AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(otlpEndpoint);
                        });
                    }
                })
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation();

                    var otlpEndpoint = builder.Configuration["OpenTelemetry:Otlp:Endpoint"];
                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        metrics.AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(otlpEndpoint);
                        });
                    }
                });

            await builder.AddApplicationAsync<BankApiAbpHttpApiHostModule>();

            var app = builder.Build();
            if (app.Environment.IsEnvironment("Test") ||
    app.Environment.IsEnvironment("RateLimitTest"))
            {
                app.Use(async (context, next) =>
                {
                    await next();

                    if (context.Response.StatusCode == StatusCodes.Status302Found &&
                        context.Response.Headers.Location.ToString().Contains("httpStatusCode=429"))
                    {
                        context.Response.Headers.Location = "";
                        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    }
                });
            }

            await app.InitializeApplicationAsync();
            await app.RunAsync();

            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "BankApiAbp.HttpApi.Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}