using Modulix.Core.Contracts.Localization;
using Modulix.Core.Settings;

namespace Modulix.Core.Localization;

/// <summary>
/// Dil tercihlerinin kaynağı (ADR-0034 §2): oturumdaki ya da işi başlatan kullanıcının tercihi ve aktif tenant'ın varsayılan dili.
/// Uygulaması sunucu katmanındadır (kullanıcı tablosu ve firma ayarı); <see cref="RequestLanguage"/> yalnızca sırayı uygular.
/// </summary>
public interface ILanguagePreferences
{
    /// <summary>Kullanıcının kayıtlı tercihi; oturum ya da tercih yoksa null.</summary>
    Task<string?> GetUserPreferenceAsync(CancellationToken cancellationToken);

    /// <summary>Aktif tenant'ın <c>core.regional.language</c> ayarı; aktif tenant yoksa null.</summary>
    Task<string?> GetTenantDefaultAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IRequestLanguage"/>: kullanıcı tercihi → tenant varsayılanı → <c>tr</c>; geçersiz değer atlanır. Tenant varsayılanı
/// yalnızca kullanıcının geçerli bir tercihi yoksa okunur. Sonuç kapsam (istek) boyunca saklanır.
/// </summary>
public sealed class RequestLanguage(ILanguagePreferences preferences) : IRequestLanguage
{
    private string? resolved;

    public async ValueTask<string> GetAsync(CancellationToken cancellationToken)
    {
        if (resolved is not null)
        {
            return resolved;
        }

        var language = LanguageOptions.Normalize(await preferences.GetUserPreferenceAsync(cancellationToken))
            ?? LanguageOptions.Normalize(await preferences.GetTenantDefaultAsync(cancellationToken))
            ?? LanguageOptions.Default;
        resolved = language;
        return language;
    }
}

/// <summary><see cref="ILocalizer"/>: sözlük + isteğin dili.</summary>
public sealed class Localizer(IRequestLanguage language, ITextCatalog catalog) : ILocalizer
{
    public ValueTask<string> GetLanguageAsync(CancellationToken cancellationToken) => language.GetAsync(cancellationToken);

    public async ValueTask<string> TextAsync(string key, CancellationToken cancellationToken, params object?[] args) =>
        catalog.Get(await language.GetAsync(cancellationToken), key, args);

    public async ValueTask<string> TextAsync(ServerMessage message, CancellationToken cancellationToken) =>
        message.Key is null ? message.Text : catalog.Translate(await language.GetAsync(cancellationToken), message);
}
