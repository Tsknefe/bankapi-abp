using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace BankApiAbp.DbMigrator;

public class TestUserPasswordSeeder : ITransientDependency
{
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionDataSeeder _permissionDataSeeder;
    private readonly ILogger<TestUserPasswordSeeder> _logger;

    public TestUserPasswordSeeder(
        IdentityUserManager userManager,
        IdentityRoleManager roleManager,
        IPermissionDataSeeder permissionDataSeeder,
        ILogger<TestUserPasswordSeeder> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _permissionDataSeeder = permissionDataSeeder;
        _logger = logger;
    }

    [UnitOfWork]
    public async Task SeedAsync()
    {
        var adminRole = await EnsureAdminRoleAsync();

        await EnsureUserAsync(
            "admin",
            "admin@bankapi.local",
            "Admin123*",
            adminRole.Name!);

        await EnsureUserAsync(
            "test_basic",
            "test_basic@bankapi.local",
            "Admin123*",
            adminRole.Name!);

        await EnsureUserAsync(
            "test_ratelimit",
            "test_ratelimit@bankapi.local",
            "Admin123*",
            adminRole.Name!);

        await EnsureUserAsync(
            "test_concurrent",
            "test_concurrent@bankapi.local",
            "Admin123*",
            adminRole.Name!);
    }

    private async Task<IdentityRole> EnsureAdminRoleAsync()
    {
        var adminRole =
            await _roleManager.FindByNameAsync("admin")
            ?? await _roleManager.FindByNameAsync("administrator");

        if (adminRole == null)
        {
            adminRole = new IdentityRole(
                Guid.Parse("99999999-9999-9999-9999-999999999999"),
                "admin");

            var createRoleResult = await _roleManager.CreateAsync(adminRole);
            createRoleResult.Succeeded.Should().BeTrue(
                string.Join(" | ", createRoleResult.Errors.Select(x => $"{x.Code}:{x.Description}")));
        }

        return adminRole;
    }

    private async Task EnsureUserAsync(
        string username,
        string email,
        string password,
        string roleName)
    {
        var user =
            await _userManager.FindByNameAsync(username)
            ?? await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            user = new IdentityUser(Guid.NewGuid(), username, email);

            var createResult = await _userManager.CreateAsync(user, password);
            createResult.Succeeded.Should().BeTrue(
                string.Join(" | ", createResult.Errors.Select(x => $"{x.Code}:{x.Description}")));

            _logger.LogInformation("Runtime user created: {Username}", username);
        }
        else
        {
            var hasPassword = await _userManager.HasPasswordAsync(user);

            if (hasPassword)
            {
                var removePasswordResult = await _userManager.RemovePasswordAsync(user);

                if (!removePasswordResult.Succeeded)
                {
                    throw new Exception(
                        $"Kullanıcının şifresi silinemedi: {username} => " +
                        string.Join(" | ", removePasswordResult.Errors.Select(x => $"{x.Code}:{x.Description}")));
                }
            }

            var addPasswordResult = await _userManager.AddPasswordAsync(user, password);
            addPasswordResult.Succeeded.Should().BeTrue(
                string.Join(" | ", addPasswordResult.Errors.Select(x => $"{x.Code}:{x.Description}")));

            _logger.LogInformation("Runtime user password reset: {Username}", username);
        }

        var inRole = await _userManager.IsInRoleAsync(user, roleName);
        if (!inRole)
        {
            var addRoleResult = await _userManager.AddToRoleAsync(user, roleName);
            addRoleResult.Succeeded.Should().BeTrue(
                string.Join(" | ", addRoleResult.Errors.Select(x => $"{x.Code}:{x.Description}")));
        }

        await _permissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            user.Id.ToString(),
            Array.Empty<string>());
    }
}