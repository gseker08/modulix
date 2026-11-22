using System.Text.RegularExpressions;

namespace Modulix.Web.Services;

/// <summary>
/// Vurgu rengi girişinin (E8 renk seçici) metin işleme kuralları. Sunucu yalnızca <c>#RRGGBB</c> kabul eder; burada kullanıcının
/// yazdığı değer o biçime yaklaştırılır (başına # eklenir, büyük harfe çevrilir). Kontrast kuralı sunucudadır (önizleme ucu).
/// </summary>
public static partial class AccentInput
{
    /// <summary>Tarayıcının renk seçicisi geçerli değer ister; renk yoksa gösterilen Modulix varsayılanı.</summary>
    public const string DefaultLight = "#087DB8";

    public const string DefaultDark = "#3BAEE6";

    /// <summary>
    /// Boşlukları atar; 6 onaltılık hane ise (başında # olsun ya da olmasın) <c>#RRGGBB</c> büyük harfe çevirir. Başka her değer
    /// olduğu gibi (kırpılmış) döner; geçerliliğine sunucu karar verir.
    /// </summary>
    public static string Normalize(string? raw)
    {
        string text = (raw ?? string.Empty).Trim();
        string digits = text.StartsWith('#') ? text[1..] : text;
        return SixHex().IsMatch(digits) ? "#" + digits.ToUpperInvariant() : text;
    }

    /// <summary>Metin tam <c>#RRGGBB</c> ise <c>true</c> (renk seçiciye ve önizlemeye verilebilir).</summary>
    public static bool IsHex(string? value) => value is { Length: 7 } && HexColor().IsMatch(value);

    /// <summary>Renk seçicinin değeri: geçerli renk, yoksa temanın varsayılanı.</summary>
    public static string PickerValue(string? value, bool dark) =>
        IsHex(value) ? value!.ToLowerInvariant() : (dark ? DefaultDark : DefaultLight).ToLowerInvariant();

    [GeneratedRegex("^[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex SixHex();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}
