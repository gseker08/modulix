using System.Globalization;
using System.Text.RegularExpressions;

namespace Modulix.Core.Settings;

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

    /// <summary>Renk geçerliyse <c>null</c>, değilse kullanıcıya gösterilecek hata. Boş değer "firma rengi yok" demektir.</summary>
    public static string? Validate(string value, bool dark)
    {
        if (value.Length == 0)
        {
            return null;
        }

        if (!HexPattern().IsMatch(value))
        {
            return "Renk #RRGGBB biçiminde olmalıdır (ör. #087DB8).";
        }

        var luminance = Luminance(value);
        var against = (dark ? DarkSurfaces : LightSurfaces).Append(dark ? DarkOnBrand : LightOnBrand);
        var worst = against.Min(other => Contrast(luminance, Luminance(other)));
        return worst >= MinContrast
            ? null
            : $"Bu renk {(dark ? "koyu" : "açık")} temada yeterince okunmuyor (kontrast {worst:0.0}:1, en az {MinContrast:0.0}:1 gerekir).";
    }

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
