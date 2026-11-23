using System.Text.Json;

namespace Modulix.Web.Services;

/// <summary>
/// Paneldeki "Sık Kullanılanlar": kullanıcının yıldızladığı ana uygulamaların kimlikleri. Kişisel bir kolaylıktır ve tarayıcıda
/// saklanır (kullanıcı + firma başına ayrı anahtar; sunucuda tutulmaz, cihazlar arasında taşınmaz). Yetki değildir: saklanan kimlik
/// yalnızca kullanıcı o uygulamayı hâlâ görebiliyorsa gösterilir.
/// </summary>
public static class LauncherFavorites
{
    /// <summary>Satır taşmasın diye en çok bu kadar uygulama yıldızlanır.</summary>
    public const int Max = 8;

    public static string StorageKey(Guid userId, Guid? tenantId) => $"modulix.launcher.favorites.{userId:N}.{tenantId?.ToString("N") ?? "none"}";

    /// <summary>Saklanan metni okur; bozuk ya da boş değer boş listedir (panel açılmaya devam eder).</summary>
    public static IReadOnlyList<string> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }

        try
        {
            List<string>? ids = JsonSerializer.Deserialize<List<string>>(stored);
            return ids is null ? [] : ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(Max).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Serialize(IReadOnlyList<string> ids) => JsonSerializer.Serialize(ids);

    /// <summary>Uygulamayı ekler ya da çıkarır; sınır doluysa ekleme yapılmaz.</summary>
    public static IReadOnlyList<string> Toggle(IReadOnlyList<string> ids, string appId)
    {
        if (ids.Contains(appId))
        {
            return ids.Where(id => id != appId).ToList();
        }

        return ids.Count >= Max ? ids : [.. ids, appId];
    }

    /// <summary>Yıldızlanan ve kullanıcının hâlâ görebildiği uygulamalar, yıldızlanma sırasıyla.</summary>
    public static IReadOnlyList<AppDefinition> Visible(IReadOnlyList<string> ids, IReadOnlyList<AppDefinition> roots) =>
        ids.Select(id => roots.FirstOrDefault(r => r.Id == id)).OfType<AppDefinition>().ToList();
}
