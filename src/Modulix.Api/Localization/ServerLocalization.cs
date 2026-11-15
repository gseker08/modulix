using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Localization;
using Modulix.Core.Modules;
using Modulix.Core.Settings;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Api.Localization;

/// <summary>
/// Sunucu dil çözümü (ADR-0034 §6, dilim 18a): kullanıcı oturumdan (isteğin kimliği, yoksa arka plan bağlamının kullanıcısı),
/// tenant aktif tenant bağlamından belirlenir; istemcinin gönderdiği başlık (<c>Accept-Language</c>) ya da parametre dili belirlemez.
/// </summary>
public static class ServerLocalizationSetup
{
    public static IServiceCollection AddModulixServerLocalization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ILanguagePreferences, SessionLanguagePreferences>();
        services.AddScoped<IRequestLanguage, RequestLanguage>();
        services.AddScoped<ILocalizer, Localizer>();
        services.AddScoped<IRecipientLanguage, RecipientLanguage>();
        return services;
    }
}

/// <summary>
/// <see cref="ILanguagePreferences"/>: kullanıcının tercihi <c>platform</c> kullanıcı tablosundan (RLS dışı), tenant varsayılanı
/// firma ayarından (<see cref="CoreModule.SettingKeys.Language"/>) okunur. Firma ayarı yalnızca aktif tenant varken çözülür
/// (tenant bağlamına bağlıdır); başka tenant'ın ayarı hiçbir yoldan okunmaz.
/// </summary>
public sealed class SessionLanguagePreferences(
    IHttpContextAccessor http, ITenantContextAccessor tenant, ModulixDbContext db, IServiceProvider services) : ILanguagePreferences
{
    public async Task<string?> GetUserPreferenceAsync(CancellationToken cancellationToken)
    {
        Guid? userId = Guid.TryParse(http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var fromSession)
            ? fromSession
            : tenant.Current?.UserId is { } fromContext && fromContext != Guid.Empty ? fromContext : null;
        if (userId is not { } id)
        {
            return null;
        }

        return await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.PreferredLanguage).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetTenantDefaultAsync(CancellationToken cancellationToken) =>
        tenant.Current is null
            ? null
            : await services.GetRequiredService<ITenantSettings>().GetValueAsync(CoreModule.SettingKeys.Language, cancellationToken);
}


/// <summary>
/// <see cref="IRecipientLanguage"/> (dilim 18b): alıcının tercihi <c>platform</c> kullanıcı tablosundan, tenant varsayılanı aktif tenant'ın
/// firma ayarından okunur; geçersiz değer atlanır. Sonuçlar kapsam boyunca tenant başına saklanır (aynı işte çok alıcıya gönderimde tekrar sorgu yok; tenant değişince yeniden okunur).
/// </summary>
public sealed class RecipientLanguage(ITenantContextAccessor tenant, ModulixDbContext db, IServiceProvider services, ITextCatalog catalog)
    : IRecipientLanguage
{
    private readonly Dictionary<(Guid? Tenant, Guid User), LocalizedTexts> users = [];
    private readonly Dictionary<Guid, LocalizedTexts> externals = [];

    public async ValueTask<LocalizedTexts> ForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = (tenant.Current?.TenantId, userId);
        if (users.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var preference = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PreferredLanguage).FirstOrDefaultAsync(cancellationToken);
        var texts = LanguageOptions.Normalize(preference) is { } language
            ? new LocalizedTexts(language, catalog)
            : await ForExternalAsync(cancellationToken);
        users[key] = texts;
        return texts;
    }

    public async ValueTask<LocalizedTexts> ForExternalAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.Current?.TenantId ?? Guid.Empty;
        if (externals.TryGetValue(tenantId, out var cached))
        {
            return cached;
        }

        var tenantDefault = tenant.Current is null
            ? null
            : await services.GetRequiredService<ITenantSettings>().GetValueAsync(CoreModule.SettingKeys.Language, cancellationToken);
        var texts = new LocalizedTexts(LanguageOptions.Normalize(tenantDefault) ?? LanguageOptions.Default, catalog);
        externals[tenantId] = texts;
        return texts;
    }
}
