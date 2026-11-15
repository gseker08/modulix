using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Notifications;
using Modulix.Infrastructure.Email;
using Modulix.Infrastructure.Identity;
using Modulix.Infrastructure.Persistence;
using Modulix.Shared.Account;
using Modulix.Shared.Auth;
using Modulix.Shared.Settings;
using Modulix.Shared.Users;
using static Modulix.Core.Modules.CoreModule.Permissions;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0034 §6, dilim 18b: e-posta, işi yapanın değil alıcının dilinde yazılır (alıcının tercihi → firma varsayılanı → Türkçe) ve
/// dil kuyruk satırında saklanır. Bir firmanın varsayılan dili başka firmanın e-postasına sızmaz.
/// </summary>
public class EmailLanguageTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    [Fact]
    public async Task Forgot_password_email_uses_the_recipients_preference_and_stores_the_language()
    {
        var tenant = await factory.CreateTenantAsync();
        var english = await factory.CreateUserAsync(tenant);
        var turkish = await factory.CreateUserAsync(tenant);
        (await LoginAsync(english, "en")).Dispose();
        using var anonymous = factory.CreateBrowserClient();

        await ForgotPasswordAsync(anonymous, english.Email!);
        await ForgotPasswordAsync(anonymous, turkish.Email!);

        var inEnglish = await LatestEmailAsync(english.Email!);
        Assert.Equal("Your Modülix password reset link", inEnglish.Subject);
        Assert.StartsWith($"Hello {english.DisplayName},", inEnglish.TextBody);
        Assert.Equal("en", inEnglish.Language);

        var inTurkish = await LatestEmailAsync(turkish.Email!);
        Assert.Equal("Modülix şifre sıfırlama bağlantınız", inTurkish.Subject);
        Assert.StartsWith($"Merhaba {turkish.DisplayName},", inTurkish.TextBody);
        Assert.Equal("tr", inTurkish.Language);
    }

    [Fact]
    public async Task Invitation_uses_the_tenant_default_for_a_new_user_and_the_preference_for_an_existing_one()
    {
        var tenant = await factory.CreateTenantAsync();
        var admin = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ManageUsers, ManageSettings], admin);
        var other = await factory.CreateTenantAsync();
        var existing = await factory.CreateUserAsync(other);
        (await LoginAsync(existing, "en")).Dispose();
        using var client = await LoginAsync(admin);
        await SetTenantLanguageAsync(client, "az");

        var newcomer = $"dil-{Guid.NewGuid():N}@test.modulix";
        await InviteAsync(client, newcomer, "Yeni Kişi");
        await InviteAsync(client, existing.Email!, existing.DisplayName);

        var invitation = await LatestEmailAsync(newcomer);
        Assert.EndsWith("sizi Modülix-ə dəvət etdi", invitation.Subject);
        Assert.Equal("az", invitation.Language);
        Assert.Equal(tenant.Id, invitation.TenantId);

        var added = await LatestEmailAsync(existing.Email!);
        Assert.StartsWith("You were added to ", added.Subject);
        Assert.Equal("en", added.Language);
    }

    [Fact]
    public async Task A_tenants_default_language_does_not_reach_another_tenants_email()
    {
        var withDefault = await factory.CreateTenantAsync();
        var withoutDefault = await factory.CreateTenantAsync();
        var first = await factory.CreateUserAsync(withDefault);
        var second = await factory.CreateUserAsync(withoutDefault);
        await factory.CreateRoleAsync(withDefault.Id, [ManageUsers, ManageSettings], first);
        await factory.CreateRoleAsync(withoutDefault.Id, [ManageUsers], second);
        using var firstClient = await LoginAsync(first);
        using var secondClient = await LoginAsync(second);
        await SetTenantLanguageAsync(firstClient, "en");

        var toFirst = $"dil-{Guid.NewGuid():N}@test.modulix";
        var toSecond = $"dil-{Guid.NewGuid():N}@test.modulix";
        await InviteAsync(firstClient, toFirst, "Birinci");
        await InviteAsync(secondClient, toSecond, "İkinci");

        Assert.Equal("en", (await LatestEmailAsync(toFirst)).Language);
        var turkish = await LatestEmailAsync(toSecond);
        Assert.Equal("tr", turkish.Language);
        Assert.EndsWith("sizi Modülix'e davet etti", turkish.Subject);
    }

    [Fact]
    public void Queue_rejects_an_invalid_language_code_and_keeps_an_empty_one_empty()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new EmailMessage("alici@test.modulix", "Konu", "<p>İçerik</p>");

        Assert.Throws<ArgumentException>(() => new QueuedEmail(message with { Language = "en-US" }, null, now));
        Assert.Throws<ArgumentException>(() => new QueuedEmail(message with { Language = "english" }, null, now));
        Assert.Null(new QueuedEmail(message, null, now).Language);
        Assert.Equal("en", new QueuedEmail(message with { Language = " EN " }, null, now).Language);
    }

    private static async Task ForgotPasswordAsync(HttpClient client, string email) =>
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email))).StatusCode);

    private static async Task SetTenantLanguageAsync(HttpClient client, string language)
    {
        var saved = await client.PutAsJsonAsync("/api/settings", new SaveSettingsRequest(
            new Dictionary<string, SettingWriteDto> { [SettingKeys.Language] = new(language, 0) }));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private static async Task InviteAsync(HttpClient client, string email, string displayName)
    {
        var response = await client.PostAsJsonAsync("/api/users/invitations", new InviteUserRequest(email, displayName, []));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<QueuedEmail> LatestEmailAsync(string address)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
        return await db.EmailMessages.AsNoTracking().Where(e => e.ToAddress == address).OrderByDescending(e => e.CreatedAt).FirstAsync();
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user, string? language = null)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (language is not null)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/account/language", new UpdateLanguageRequest(language))).StatusCode);
        }

        return client;
    }
}
