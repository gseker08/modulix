using System.Globalization;
using Modulix.Shared.Localization;
using Modulix.Shared.Settings;

namespace Modulix.Web.Services;

/// <summary>
/// Oturumsuz müşteri sayfalarının dili (ADR-0034 §6, dilim 22b). Sayfa önce tarayıcının diliyle açılır; sunucu kaydı çözünce
/// müşterinin dili (kişinin yazışma dili → firmasınınki → tenant varsayılanı) yanıtta gelir ve sayfa o dilin sözlüğüne geçer.
/// Sözlük yüklenemezse eldeki kalır.
/// </summary>
public static class PublicTexts
{
    /// <param name="language">Sunucunun bildirdiği müşteri dili; boşsa tarayıcının dili, o da desteklenmiyorsa Türkçe.</param>
    public static async Task ApplyAsync(ApiClient api, string? language)
    {
        var code = LanguageCodes.Normalize(language) ?? LanguageCodes.FromCulture(CultureInfo.CurrentUICulture.Name) ?? LanguageCodes.Default;
        UiLanguage.Apply(code, null);
        if (Texts.Language == code)
        {
            return;
        }

        var bundle = await api.GetAsync<TextBundleResponse>($"/api/i18n/{code}");
        if (bundle.Value is { } loaded)
        {
            Texts.Apply(loaded);
        }
    }
}
