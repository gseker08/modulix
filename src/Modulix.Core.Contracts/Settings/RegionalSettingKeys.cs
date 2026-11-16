namespace Modulix.Core.Contracts.Settings;

/// <summary>
/// Firmanın bölgesel ayar anahtarları (Core'a aittir; modüller <see cref="ITenantSettingReader"/> ile okur, ADR-0046). Saat dilimi için
/// doğrulanmış değer gerekiyorsa <c>ITenantClock</c> kullanılır.
/// </summary>
public static class RegionalSettingKeys
{
    public const string TimeZone = "core.regional.time-zone";

    public const string Currency = "core.regional.currency";

    /// <summary>Ayar boşken ya da geçersizken (üç harf değilse) kullanılan para birimi.</summary>
    public const string DefaultCurrency = "TRY";
}

/// <summary>Firmanın profil ayar anahtarları (Core'a aittir; modüller <see cref="ITenantSettingReader"/> ile okur, ADR-0046).</summary>
public static class ProfileSettingKeys
{
    /// <summary>Belgelerde ve müşteriye giden iletilerde görünen firma adı; boşsa firmanın kayıtlı adı kullanılır (<c>ICurrentTenantInfo</c>).</summary>
    public const string DisplayName = "core.profile.display-name";
}
