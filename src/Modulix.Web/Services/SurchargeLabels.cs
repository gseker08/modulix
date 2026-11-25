using System.Text.Json.Nodes;

namespace Modulix.Web.Services;

/// <summary>Ek ücret kuralı ekranının Türkçe etiketleri ve metin dönüşümleri (ADR-0018 devamı, dilim 2d); anahtarlar sunucudaki kural sözlüğüdür.</summary>
public static class SurchargeLabels
{
    public static IReadOnlyList<(string Key, string Label)> Kinds { get; } =
    [
        ("Fixed", "Sabit tutar"),
        ("Percent", "Yüzde"),
        ("Minimum", "Asgari bedel"),
        ("Duration", "Süreye bağlı"),
    ];

    public static IReadOnlyList<(string Key, string Label)> Presentations { get; } =
    [
        ("SeparateLine", "Ayrı satır"),
        ("IncludedInGroup", "Grubun içinde"),
        ("BreakdownOnly", "Yalnızca dökümde"),
    ];

    public static IReadOnlyList<(string Key, string Label)> Operators { get; } =
    [
        ("Equals", "eşittir"),
        ("NotEquals", "eşit değildir"),
        ("GreaterThan", "büyüktür"),
        ("GreaterOrEqual", "büyük veya eşittir"),
        ("LessThan", "küçüktür"),
        ("LessOrEqual", "küçük veya eşittir"),
    ];

    public static string Kind(string key) => Find(Kinds, key);

    public static string Presentation(string key) => Find(Presentations, key);

    public static string Operator(string key) => Find(Operators, key);

    /// <summary>Kuralın ne hesapladığını tek satırda özetler (liste sütunu).</summary>
    public static string Summary(string kind, string currency, decimal? amount, decimal? percent, string? group, string? baseKey, string? durationKey, decimal freeUnits) => kind switch
    {
        "Fixed" => $"{Money(amount, currency)} sabit",
        "Percent" => $"%{Number(percent)} × {baseKey ?? "?"}",
        "Minimum" => $"{group ?? "?"} grubu en az {Money(amount, currency)}",
        "Duration" => $"{durationKey ?? "?"} süresi, ilk {Number(freeUnits)} birim ücretsiz, sonrası "
            + (amount is not null ? Money(amount, currency) : $"%{Number(percent)} × {baseKey ?? "?"}"),
        _ => kind,
    };

    public static string Money(decimal? value, string currency) => $"{Number(value)} {currency}";

    public static string Number(decimal? value) => (value ?? 0m).ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

    /// <summary>"a, b" metnini kararlı sırayla anahtar listesine çevirir; boş girdi boş dizi verir.</summary>
    public static JsonArray ParseKeys(string? text) =>
        [.. (text ?? string.Empty).Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).Select(k => (JsonNode)JsonValue.Create(k)!)];

    public static string JoinKeys(JsonArray? keys) =>
        keys is null ? string.Empty : string.Join(", ", keys.Select(k => k?.GetValue<string>()).Where(k => !string.IsNullOrEmpty(k)));

    private static string Find(IReadOnlyList<(string Key, string Label)> items, string key) =>
        items.FirstOrDefault(i => i.Key == key).Label ?? key;
}
