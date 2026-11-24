using System.Net.Mail;
using System.Text.RegularExpressions;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Modules;

namespace Modulix.Core.Settings;

/// <summary>Ayar değerinin tanımına uygunluğu (Y6). Hem ayarlar uç noktası hem kayıt deposu bunu kullanır.</summary>
public static partial class SettingRules
{
    /// <summary>Değer geçerliyse <c>null</c>, değilse kullanıcıya gösterilecek hata; ileti HTTP sınırında isteğin diline çevrilir (ADR-0034 §6).</summary>
    public static ServerMessage? Validate(SettingDefinition definition, string? value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (value is null)
        {
            return ServerMessages.Setting.ValueText;
        }

        if (value.Length == 0)
        {
            return definition.Required ? ServerMessages.Validation.Required : null;
        }

        if (value != value.Trim())
        {
            return ServerMessages.Setting.ValueTrim;
        }

        if (value.Length > Math.Min(definition.MaxLength, TenantSetting.ValueMaxLength))
        {
            return ServerMessages.Validation.MaxLength(definition.MaxLength);
        }

        if (definition.Type != SettingType.MultilineText && value.Contains('\n', StringComparison.Ordinal))
        {
            return ServerMessages.Setting.SingleLine;
        }

        // Vurgu rengi (E8): biçim ve açık/koyu temadaki kontrast sunucuda denetlenir; ileti sözlük anahtarlıdır.
        if (definition.Key is CoreModule.SettingKeys.AccentLight or CoreModule.SettingKeys.AccentDark)
        {
            return AccentColor.Validate(value, dark: definition.Key == CoreModule.SettingKeys.AccentDark);
        }

        return definition.Type switch
        {
            SettingType.Choice when definition.Options.All(o => o.Value != value) => ServerMessages.Setting.Choice,
            SettingType.Email when !IsEmail(value) => ServerMessages.Validation.Email,
            SettingType.Phone when !PhonePattern().IsMatch(value) => ServerMessages.Validation.Phone,
            _ when definition.Pattern is { } pattern && !Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) =>
                definition.PatternMessage is { } custom
                ? ServerMessage.Of(MetadataTexts.SettingPatternMessageKey(definition.Key), custom)
                : ServerMessages.Setting.Pattern,
            _ => null,
        };
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value && value.Contains('.', StringComparison.Ordinal);

    // Rakam, boşluk, artı, tire ve parantez; en az 7 rakam.
    [GeneratedRegex(@"^(?=(?:\D*\d){7})[0-9 +()\-]{7,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
