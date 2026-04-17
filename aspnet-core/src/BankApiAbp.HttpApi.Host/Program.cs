using System;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using BankApiAbp.Banking;
using BankApiAbp.Banking.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
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
    public static async Task<int> Main(string[] args)
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        Log.Logger = CreateBootstrapLogger();

        try
        {
            Log.Information("Starting BankApiAbp.HttpApi.Host.");

            var builder = WebApplication.CreateBuilder(args);

            builder.Host.UseAutofac();
            builder.Host.UseSerilog();

            ConfigureSerilogWithOptionalElastic(builder);

            var environmentName = builder.Environment.EnvironmentName;
            var contentRoot = builder.Environment.ContentRootPath;

            var otlpEndpoint =
                builder.Configuration["OpenTelemetry:Otlp:Endpoint"] ??
                "http://localhost:4317";

            Console.WriteLine($"ENVIRONMENT = {environmentName}");
            Console.WriteLine($"CONTENT ROOT = {contentRoot}");
            Console.WriteLine($"OTLP ENDPOINT = {otlpEndpoint}");

            Log.Information("Environment: {EnvironmentName}", environmentName);
            Log.Information("ContentRoot: {ContentRoot}", contentRoot);
            Log.Information("OTLP endpoint: {OtlpEndpoint}", otlpEndpoint);

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
                    var globalPermit =
                        builder.Configuration.GetValue<int?>("RateLimiting:Global:PermitLimit") ?? 100;

                    var globalWindowSeconds =
                        builder.Configuration.GetValue<int?>("RateLimiting:Global:WindowSeconds") ?? 60;

                    var transferTokenLimit =
                        builder.Configuration.GetValue<int?>("RateLimiting:TransferPolicy:TokenLimit")
                        ?? builder.Configuration.GetValue<int?>("RateLimiting:Transfer:TokenLimit")
                        ?? 5;

                    var transferTokensPerPeriod =
                        builder.Configuration.GetValue<int?>("RateLimiting:TransferPolicy:TokensPerPeriod")
                        ?? builder.Configuration.GetValue<int?>("RateLimiting:Transfer:TokensPerPeriod")
                        ?? 5;

                    var transferPeriodSeconds =
                        builder.Configuration.GetValue<int?>("RateLimiting:TransferPolicy:PeriodSeconds")
                        ?? builder.Configuration.GetValue<int?>("RateLimiting:Transfer:PeriodSeconds")
                        ?? 60;

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

            builder.Services
                .AddOpenTelemetry()
                .ConfigureResource(resource =>
                {
                    resource.AddService("BankApiAbp.HttpApi.Host");
                })
                .WithTracing(tracing =>
                {
                    tracing
                        .AddSource(InboxTracing.ActivitySourceName)
                        .AddSource(BankingAppService.ActivitySourceName)
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddEntityFrameworkCoreInstrumentation(options =>
                        {
                            options.SetDbStatementForText = true;
                            options.SetDbStatementForStoredProcedure = true;
                        })
                        .AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(otlpEndpoint);
                            options.Protocol = OtlpExportProtocol.Grpc;
                        });
                })
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddMeter(InboxMetrics.MeterName)
                        .AddMeter(BankingAppService.MeterName)
                        .AddPrometheusExporter()
                        .AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(otlpEndpoint);
                            options.Protocol = OtlpExportProtocol.Grpc;
                        });
                });

            await builder.AddApplicationAsync<BankApiAbpHttpApiHostModule>();

            var app = builder.Build();

            app.UseOpenTelemetryPrometheusScrapingEndpoint();

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

            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
                    var exception = exceptionFeature?.Error;

                    if (exception != null)
                    {
                        Log.Error(exception, "Unhandled exception caught by global exception handler.");
                    }

                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        context.Response.ContentType = "application/json";

                        await context.Response.WriteAsync("""
                        {
                          "error": {
                            "message": "Beklenmeyen bir sunucu hatası oluştu."
                          }
                        }
                        """);
                    }
                });
            });

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

    private static ILogger CreateBootstrapLogger()
    {
        return new LoggerConfiguration()
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
    }

    private static void ConfigureSerilogWithOptionalElastic(WebApplicationBuilder builder)
    {
        var elasticUri =
            builder.Configuration["Elastic:Uri"] ??
            builder.Configuration["ElasticConfiguration:Uri"];

        if (string.IsNullOrWhiteSpace(elasticUri))
            return;

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
}