using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace BankApiAbp.DbMigrator;

public class TestUserPasswordSeeder : ITransientDependency
{
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityUserRepository _userRepository;

    public TestUserPasswordSeeder(
        IdentityUserManager userManager,
        IIdentityUserRepository userRepository)
    {
        _userManager = userManager;
        _userRepository = userRepository;
    }

    public async Task SeedAsync()
    {
        var user = await _userRepository.FindByNormalizedUserNameAsync("EFE");
        if (user == null)
        {
            throw new Exception("EFE kullanıcısı bulunamadı.");
        }

        var hasPassword = await _userManager.HasPasswordAsync(user);
        if (hasPassword)
        {
            var removeResult = await _userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
            {
                throw new Exception(
                    "Eski şifre kaldırılamadı: " +
                    string.Join(" | ", removeResult.Errors.Select(x => x.Description))
                );
            }
        }

        var addResult = await _userManager.AddPasswordAsync(user, "Qwe123!");
        if (!addResult.Succeeded)
        {
            throw new Exception(
                "Yeni şifre eklenemedi: " +
                string.Join(" | ", addResult.Errors.Select(x => x.Description))
            );
        }

        await _userRepository.UpdateAsync(user, autoSave: true);
    }
}