using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenIddict.Abstractions;
using Volo.Abp.DependencyInjection;

namespace BankApiAbp.DbMigrator;

public class OpenIddictSwaggerClientSeeder : ITransientDependency
{
    private readonly IOpenIddictApplicationManager _appManager;
    private readonly IConfiguration _configuration;

    public OpenIddictSwaggerClientSeeder(
        IOpenIddictApplicationManager appManager,
        IConfiguration configuration)
    {
        _appManager = appManager;
        _configuration = configuration;
    }

    public async Task SeedAsync()
    {
        var section = _configuration.GetSection("OpenIddict:Applications:BankApiAbp_Swagger");
        var clientId = section["ClientId"] ?? "BankApiAbp_Swagger";
        var rootUrl = section["RootUrl"] ?? "https://localhost:44389";

        var existing = await _appManager.FindByClientIdAsync(clientId);
        if (existing != null)
            return;

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = "Swagger UI",
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit
        };

        descriptor.RedirectUris.Add(new Uri($"{rootUrl.TrimEnd('/')}/swagger/oauth2-redirect.html"));
        descriptor.PostLogoutRedirectUris.Add(new Uri($"{rootUrl.TrimEnd('/')}/swagger/"));

        descriptor.Permissions.UnionWith(new[]
        {
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
            OpenIddictConstants.Permissions.ResponseTypes.Code,

            OpenIddictConstants.Permissions.Prefixes.Scope + "openid",
            OpenIddictConstants.Permissions.Prefixes.Scope + "profile",
            OpenIddictConstants.Permissions.Prefixes.Scope + "email",
            OpenIddictConstants.Permissions.Prefixes.Scope + "roles",
            OpenIddictConstants.Permissions.Prefixes.Scope + "offline_access",
            OpenIddictConstants.Permissions.Prefixes.Scope + "BankApiAbp"
        });

        descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);

        await _appManager.CreateAsync(descriptor);
    }
}
