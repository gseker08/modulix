using Modulix.Modules.Crm.Domain;

namespace Modulix.Modules.Crm.Leads;

/// <summary>
/// Müşteri adayı puan hesabı (ADR-0050, dilim 1). Saf fonksiyondur: adayı ve etkin kuralları alır, eşleşen kuralların puan toplamını 0..100
/// aralığına kırparak döner. Puanlanabilir alanlar beyaz listedir; tahmini bütçe hassas alan olduğu için listede yoktur (puan bütçeyi sezdirir).
/// </summary>
public static class LeadScoring
{
    public const int MinScore = 0;
    public const int MaxScore = 100;

    /// <summary>Alanın karşılaştırma biçimi; hangi işleçlerin geçerli olduğunu belirler.</summary>
    public enum FieldKind
    {
        /// <summary>Kod ya da seçim anahtarı: tam eşleşme (büyük/küçük harf duyarsız).</summary>
        Code,

        /// <summary>Serbest metin: tam eşleşme ya da içerir.</summary>
        Text,

        /// <summary>Virgüllü kod listesi (hizmet kapsamı): içerir = listede bu kod var.</summary>
        CodeList,

        /// <summary>Yalnızca dolu/boş denetimi.</summary>
        Presence,
    }

    public sealed record ScoreField(string Key, string Label, FieldKind Kind, Func<Lead, string?> Read);

    public static readonly IReadOnlyList<ScoreField> Fields =
    [
        new("source", "Kaynak", FieldKind.Code, l => l.Source),
        new("industry", "Sektör", FieldKind.Code, l => l.IndustryCode),
        new("country", "Ülke", FieldKind.Code, l => l.Country),
        new("city", "Şehir", FieldKind.Text, l => l.City),
        new("priority", "Öncelik", FieldKind.Code, l => l.Priority),
        new("jobTitle", "Unvan", FieldKind.Text, l => l.JobTitle),
        new("companyName", "Firma adı", FieldKind.Text, l => l.CompanyName),
        new("productInterest", "İlgilenilen ürün/hizmet", FieldKind.Text, l => l.ProductInterest),
        new("serviceScope", "Hizmet kapsamı", FieldKind.CodeList, l => l.ServiceScope),
        new("utmSource", "UTM kaynak", FieldKind.Code, l => l.UtmSource),
        new("utmMedium", "UTM ortam", FieldKind.Code, l => l.UtmMedium),
        new("utmCampaign", "UTM kampanya", FieldKind.Code, l => l.UtmCampaign),
        new("email", "E-posta", FieldKind.Presence, l => l.Email),
        new("phone", "Telefon", FieldKind.Presence, l => l.Phone),
        new("website", "Web sitesi", FieldKind.Presence, l => l.Website),
        new("companyId", "Mevcut firma", FieldKind.Presence, l => l.CompanyId?.ToString()),
        new("campaignId", "Kampanya", FieldKind.Presence, l => l.CampaignId?.ToString()),
    ];

    private static readonly Dictionary<string, ScoreField> ByKey = Fields.ToDictionary(f => f.Key, StringComparer.Ordinal);

    public static ScoreField? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var field) ? field : null;

    /// <summary>Alan türüne göre geçerli işleçler.</summary>
    public static IReadOnlyList<string> OperatorsFor(FieldKind kind) => kind switch
    {
        FieldKind.Code => [LeadScoreOperator.EqualsTo, LeadScoreOperator.NotEquals, LeadScoreOperator.IsSet, LeadScoreOperator.IsEmpty],
        FieldKind.Text => LeadScoreOperator.All,
        FieldKind.CodeList => [LeadScoreOperator.Contains, LeadScoreOperator.IsSet, LeadScoreOperator.IsEmpty],
        _ => [LeadScoreOperator.IsSet, LeadScoreOperator.IsEmpty],
    };

    /// <summary>Etkin kuralların puan toplamı, 0..100 aralığına kırpılmış. Bilinmeyen alan ya da işleç taşıyan kural eşleşmez.</summary>
    public static int Calculate(Lead lead, IEnumerable<LeadScoreRule> rules)
    {
        var total = rules.Where(r => r.IsActive && Matches(lead, r)).Sum(r => r.Points);
        return Math.Clamp(total, MinScore, MaxScore);
    }

    public static bool Matches(Lead lead, LeadScoreRule rule)
    {
        if (Find(rule.Field) is not { } field || !OperatorsFor(field.Kind).Contains(rule.Operator))
        {
            return false;
        }

        var actual = field.Read(lead)?.Trim();
        var isSet = !string.IsNullOrEmpty(actual);
        var expected = rule.Value?.Trim() ?? string.Empty;
        return rule.Operator switch
        {
            LeadScoreOperator.IsSet => isSet,
            LeadScoreOperator.IsEmpty => !isSet,
            LeadScoreOperator.EqualsTo => isSet && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            // "Eşit değil" boş alanda da sağlanır: değer yoksa beklenen değer de değildir.
            LeadScoreOperator.NotEquals => !string.Equals(actual ?? string.Empty, expected, StringComparison.OrdinalIgnoreCase),
            LeadScoreOperator.Contains when field.Kind == FieldKind.CodeList =>
                isSet && actual!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Contains(expected, StringComparer.OrdinalIgnoreCase),
            LeadScoreOperator.Contains => isSet && expected.Length > 0 && actual!.Contains(expected, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
