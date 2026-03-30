using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.EntityFrameworkCore;
using BankApiAbp.MultiTenancy;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.AntiForgery;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Caching;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EventBus.RabbitMq;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Swashbuckle;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.VirtualFileSystem;

namespace BankApiAbp;

[DependsOn(
    typeof(BankApiAbpHttpApiModule),
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(BankApiAbpApplicationModule),
    typeof(BankApiAbpEntityFrameworkCoreModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpDistributedLockingModule),
    typeof(AbpCachingStackExchangeRedisModule),
    typeof(AbpEventBusRabbitMqModule)
)]
public class BankApiAbpHttpApiHostModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("BankApiAbp");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        PreConfigure<OpenIddictServerBuilder>(builder =>
        {
            builder.AllowPasswordFlow();
            builder.AcceptAnonymousClients();
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var env = context.Services.GetHostingEnvironment();

        ConfigureAuthentication(context);
        ConfigureBundles();
        ConfigureUrls(configuration);
        ConfigureConventionalControllers();
        ConfigureVirtualFileSystem(context);
        ConfigureCors(context, configuration);
        ConfigureSwaggerServices(context, configuration);
        ConfigureRedis(context, configuration);

        Configure<OpenIddictServerBuilder>(builder =>
        {
            builder.AddDevelopmentEncryptionCertificate()
                   .AddDevelopmentSigningCertificate();
        });

        Configure<AbpAntiForgeryOptions>(options =>
        {
            options.AutoValidate = false;
        });

        Configure<AbpExceptionHttpStatusCodeOptions>(options =>
        {
            options.Map("IDEMPOTENCY_KEY_REUSE_WITH_DIFFERENT_PAYLOAD", System.Net.HttpStatusCode.Conflict);
            options.Map("IDEMPOTENCY_IN_PROGRESS", System.Net.HttpStatusCode.Conflict);
        });

        context.Services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            };
        });

        if (env.IsEnvironment("Test"))
        {
            Configure<AbpBackgroundJobOptions>(options =>
            {
                options.IsJobExecutionEnabled = false;
            });
        }
    }

    private void ConfigureRedis(ServiceConfigurationContext context, IConfiguration configuration)
    {
        var redisCfg = configuration["Redis:Configuration"];

        if (string.IsNullOrWhiteSpace(redisCfg))
            return;

        context.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisCfg));

        Configure<AbpDistributedCacheOptions>(options =>
        {
            options.KeyPrefix = "BankApiAbp:";
        });

        context.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisCfg;
        });

        Configure<AbpDistributedLockOptions>(options =>
        {
            options.KeyPrefix = "BankApiAbp:";
        });

        context.Services.AddSingleton<IDistributedLockProvider>(sp =>
        {
            var mux = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisDistributedSynchronizationProvider(mux.GetDatabase());
        });
    }

    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.ForwardIdentityAuthenticationForBearer(
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

        context.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });
    }

    private void ConfigureBundles()
    {
        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                LeptonXLiteThemeBundles.Styles.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-styles.css");
                });
        });
    }

    private void ConfigureUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            options.RedirectAllowedUrls.AddRange(
                configuration["App:RedirectAllowedUrls"]?.Split(',') ?? Array.Empty<string>());

            options.Applications["Angular"].RootUrl = configuration["App:ClientUrl"];
            options.Applications["Angular"].Urls[AccountUrlNames.PasswordReset] = "account/reset-password";
        });
    }

    private void ConfigureVirtualFileSystem(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        if (hostingEnvironment.IsDevelopment())
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.ReplaceEmbeddedByPhysical<BankApiAbpDomainSharedModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}BankApiAbp.Domain.Shared"));

                options.FileSets.ReplaceEmbeddedByPhysical<BankApiAbpDomainModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}BankApiAbp.Domain"));

                options.FileSets.ReplaceEmbeddedByPhysical<BankApiAbpApplicationContractsModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}BankApiAbp.Application.Contracts"));

                options.FileSets.ReplaceEmbeddedByPhysical<BankApiAbpApplicationModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}BankApiAbp.Application"));
            });
        }
    }

    private void ConfigureConventionalControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(BankApiAbpApplicationModule).Assembly);
        });
    }

    private static void ConfigureSwaggerServices(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddAbpSwaggerGenWithOAuth(
            configuration["AuthServer:Authority"]!,
            new Dictionary<string, string>
            {
                { "BankApiAbp", "BankApiAbp API" }
            },
            options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "BankApiAbp API",
                    Version = "v1"
                });

                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);
                options.OperationFilter<BankApiAbp.Swagger.IdempotencyHeaderOperationFilter>();
            });
    }

    private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder
                    .WithOrigins(
                        configuration["App:CorsOrigins"]?
                            .Split(",", StringSplitOptions.RemoveEmptyEntries)
                            .Select(o => o.RemovePostFix("/"))
                            .ToArray() ?? Array.Empty<string>())
                    .WithAbpExposedHeaders()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAbpRequestLocalization();

        if (!env.IsDevelopment() && !env.IsEnvironment("Test"))
        {
            app.UseErrorPage();
        }

        app.UseCorrelationId();
        app.MapAbpStaticAssets();
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();

        if (MultiTenancyConsts.IsEnabled)
        {
            app.UseMultiTenancy();
        }

        app.UseRateLimiter();
        app.UseUnitOfWork();
        app.UseDynamicClaims();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseAbpSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "BankApiAbp API");

            var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
            c.OAuthClientId(configuration["AuthServer:SwaggerClientId"]);
            c.OAuthScopes("BankApiAbp");
            c.OAuthAdditionalQueryStringParams(new Dictionary<string, string>
            {
                ["audience"] = "BankApiAbp"
            });
            c.OAuthUsePkce();
        });

        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }
}