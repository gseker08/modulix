using System.Globalization;
using System.Text.RegularExpressions;
using Modulix.Core.Contracts.Localization;

namespace Modulix.Core.Settings;

/// <summary>Vurgu renginin kontrol sonucu: geçerlilik, en düşük kontrast oranı (varsa) ve hata iletisi (geçmiyorsa).</summary>
public sealed record AccentCheck(bool Valid, double? Contrast, ServerMessage? Error)
{
    /// <summary>Hata iletisinin Türkçe kaynak metni; çevrilmesi gereken yerde <see cref="Error"/> kullanılır.</summary>
    public string? Message => Error?.Text;
}

/// <summary>
/// Firmanın vurgu rengi kuralları (E8; <c>docs/design-system/theme-tokens.md</c> "Firmanın değiştirebileceği tokenlar").
/// Renk <c>#RRGGBB</c> olmalı ve açık/koyu temanın yüzeyleriyle ve marka üstü metinle en az 4.5:1 (WCAG AA) vermelidir.
/// Yüzey değerleri <c>tokens.css</c>'tekilerdir; orada değişirse burada da değişir (birim test aynı değerleri sabitler).
/// </summary>
public static partial class AccentColor
{
    public const double MinContrast = 4.5;

    public const string Pattern = "^#[0-9A-Fa-f]{6}$";

    // Açık temada kart/panel yüzeyi (--mx-surface) ve marka üstü metin aynı beyazdır; varsayılan marka rengi de ancak bunu (4.53:1) karşılar.
    private static readonly string[] LightSurfaces = ["#FFFFFF"];
    private const string LightOnBrand = "#FFFFFF";

    private static readonly string[] DarkSurfaces = ["#121D33", "#0B1322", "#192842"];
    private const string DarkOnBrand = "#07111F";

    /// <summary>Renk geçerliyse <c>null</c>, değilse kullanıcıya gösterilecek (çevrilebilir) hata. Boş değer "firma rengi yok" demektir.</summary>
    public static ServerMessage? Validate(string value, bool dark) => Check(value, dark).Error;

    /// <summary>
    /// Rengin sonucu: geçerli mi, hesaplanabildiyse en düşük kontrast (biçim hatalıysa ve boş değerde <c>null</c>) ve geçmiyorsa
    /// hata metni. Kaydetme doğrulaması (<see cref="Validate"/>) ve ayarlar ekranındaki anlık önizleme aynı hesabı kullanır.
    /// </summary>
    public static AccentCheck Check(string value, bool dark)
    {
        if (value.Length == 0)
        {
            return new AccentCheck(true, null, null);
        }

        if (!HexPattern().IsMatch(value))
        {
            return new AccentCheck(false, null, ServerMessages.Setting.AccentFormat);
        }

        var luminance = Luminance(value);
        var against = (dark ? DarkSurfaces : LightSurfaces).Append(dark ? DarkOnBrand : LightOnBrand);
        var worst = against.Min(other => Contrast(luminance, Luminance(other)));
        return worst >= MinContrast
            ? new AccentCheck(true, worst, null)
            : new AccentCheck(false, worst, dark
                ? ServerMessages.Setting.AccentLowContrastDark(Ratio(worst), Ratio(MinContrast))
                : ServerMessages.Setting.AccentLowContrastLight(Ratio(worst), Ratio(MinContrast)));
    }

    private static string Ratio(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>WCAG göreli parlaklığı (0–1). <paramref name="hex"/> <c>#RRGGBB</c> olmalıdır.</summary>
    public static double Luminance(string hex)
    {
        double Channel(int start)
        {
            var c = int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(1)) + (0.7152 * Channel(3)) + (0.0722 * Channel(5));
    }

    public static double Contrast(string first, string second) => Contrast(Luminance(first), Luminance(second));

    private static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex HexPattern();
}
