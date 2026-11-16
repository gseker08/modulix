using System.Globalization;
using Modulix.Modules.Crm.Domain;

namespace Modulix.Modules.Crm.Leads;

/// <summary>
/// Müşteri adayı puan hesabı (ADR-0050). Saf fonksiyondur: adayı, adayın davranış sayaçlarını (<see cref="LeadBehavior"/>, dilim 2) ve etkin
/// kuralları alır, eşleşen kuralların puan toplamını 0..100 aralığına kırparak döner. Puanlanabilir alanlar beyaz listedir; tahmini bütçe hassas
/// alan olduğu için listede yoktur (puan bütçeyi sezdirir).
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

        /// <summary>Davranış sayacı (dilim 2): "en az N".</summary>
        Count,
    }

    public sealed record ScoreField(string Key, string Label, FieldKind Kind, Func<Lead, LeadBehavior, string?> Read);

    public static readonly IReadOnlyList<ScoreField> Fields =
    [
        new("source", "Kaynak", FieldKind.Code, (l, _) => l.Source),
        new("industry", "Sektör", FieldKind.Code, (l, _) => l.IndustryCode),
        new("country", "Ülke", FieldKind.Code, (l, _) => l.Country),
        new("city", "Şehir", FieldKind.Text, (l, _) => l.City),
        new("priority", "Öncelik", FieldKind.Code, (l, _) => l.Priority),
        new("jobTitle", "Unvan", FieldKind.Text, (l, _) => l.JobTitle),
        new("companyName", "Firma adı", FieldKind.Text, (l, _) => l.CompanyName),
        new("productInterest", "İlgilenilen ürün/hizmet", FieldKind.Text, (l, _) => l.ProductInterest),
        new("serviceScope", "Hizmet kapsamı", FieldKind.CodeList, (l, _) => l.ServiceScope),
        new("utmSource", "UTM kaynak", FieldKind.Code, (l, _) => l.UtmSource),
        new("utmMedium", "UTM ortam", FieldKind.Code, (l, _) => l.UtmMedium),
        new("utmCampaign", "UTM kampanya", FieldKind.Code, (l, _) => l.UtmCampaign),
        new("email", "E-posta", FieldKind.Presence, (l, _) => l.Email),
        new("phone", "Telefon", FieldKind.Presence, (l, _) => l.Phone),
        new("website", "Web sitesi", FieldKind.Presence, (l, _) => l.Website),
        new("companyId", "Mevcut firma", FieldKind.Presence, (l, _) => l.CompanyId?.ToString()),
        new("campaignId", "Kampanya", FieldKind.Presence, (l, _) => l.CampaignId?.ToString()),
        new("contacts", "Gerçekleşen temas sayısı", FieldKind.Count, (_, b) => Number(b.Contacts)),
        new("meetings", "Yapılan toplantı sayısı", FieldKind.Count, (_, b) => Number(b.Meetings)),
        new("repeatInquiries", "Tekrar başvuru sayısı", FieldKind.Count, (_, b) => Number(b.RepeatInquiries)),
        new("inactiveDays", "Hareketsiz gün sayısı", FieldKind.Count, (_, b) => Number(b.InactiveDays)),
    ];

    private static readonly Dictionary<string, ScoreField> ByKey = Fields.ToDictionary(f => f.Key, StringComparer.Ordinal);

    public static ScoreField? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var field) ? field : null;

    /// <summary>Kurallardan biri davranış sayacı kullanıyor mu? Kullanmıyorsa sayaçlar okunmaz (hesap yalnızca adayın alanlarıyla yapılır).</summary>
    public static bool NeedsBehavior(IEnumerable<LeadScoreRule> rules) =>
        rules.Any(r => r.IsActive && Find(r.Field)?.Kind == FieldKind.Count);

    /// <summary>Alan türüne göre geçerli işleçler.</summary>
    public static IReadOnlyList<string> OperatorsFor(FieldKind kind) => kind switch
    {
        FieldKind.Code => [LeadScoreOperator.EqualsTo, LeadScoreOperator.NotEquals, LeadScoreOperator.IsSet, LeadScoreOperator.IsEmpty],
        FieldKind.Text => [LeadScoreOperator.EqualsTo, LeadScoreOperator.NotEquals, LeadScoreOperator.Contains, LeadScoreOperator.IsSet, LeadScoreOperator.IsEmpty],
        FieldKind.CodeList => [LeadScoreOperator.Contains, LeadScoreOperator.IsSet, LeadScoreOperator.IsEmpty],
        FieldKind.Count => [LeadScoreOperator.AtLeast],
        _ => [LeadScoreOperator.IsSet, LeadScoreOperator.IsEmpty],
    };

    /// <summary>"En az" değeri: 1..<see cref="LeadScoreOperator.MaxCount"/> tam sayı; değilse null.</summary>
    public static int? ParseCount(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count is >= 1 and <= LeadScoreOperator.MaxCount
            ? count
            : null;

    /// <summary>Etkin kuralların puan toplamı, 0..100 aralığına kırpılmış. Bilinmeyen alan ya da işleç taşıyan kural eşleşmez.</summary>
    public static int Calculate(Lead lead, IEnumerable<LeadScoreRule> rules, LeadBehavior? behavior = null)
    {
        var facts = behavior ?? LeadBehavior.None;
        var total = rules.Where(r => r.IsActive && Matches(lead, facts, r)).Sum(r => r.Points);
        return Math.Clamp(total, MinScore, MaxScore);
    }

    public static bool Matches(Lead lead, LeadScoreRule rule) => Matches(lead, LeadBehavior.None, rule);

    public static bool Matches(Lead lead, LeadBehavior behavior, LeadScoreRule rule)
    {
        if (Find(rule.Field) is not { } field || !OperatorsFor(field.Kind).Contains(rule.Operator))
        {
            return false;
        }

        var actual = field.Read(lead, behavior)?.Trim();
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
            LeadScoreOperator.AtLeast => ParseCount(expected) is { } minimum
                && int.TryParse(actual, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= minimum,
            _ => false,
        };
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Adayın davranış sayaçları (ADR-0050, dilim 2). Adayın alanı değildir; aday puanlanırken CRM'in kendi tablolarından türetilir
/// (<c>Records/LeadBehaviorReader</c>), saklanmaz. Böylece kural değişince ya da aktivite silinince sayaç kendiliğinden doğru kalır.
/// <list type="bullet">
///   <item><see cref="Contacts"/>: adaya bağlı, tamamlanmış ve gerçekleşmiş temas sayılan aktiviteler (<see cref="LeadRules.CountsAsContact"/>).</item>
///   <item><see cref="Meetings"/>: bunların toplantı olanları.</item>
///   <item><see cref="RepeatInquiries"/>: aynı e-posta ya da telefonla gelen diğer müşteri adayları (durumu ne olursa olsun).</item>
///   <item><see cref="InactiveDays"/> (dilim 3): son gerçekleşen temastan, temas yoksa adayın oluşturulmasından bu yana geçen tam gün. Eksi puanlı
///   "en az N gün" kuralı zamanla azalmayı ifade eder; gün geçtikçe değiştiği için toplu yeniden puanlama işi yansıtır.</item>
/// </list>
/// Özel (gizli) aktiviteler sayılmaz: puan, aktiviteyi göremeyen kullanıcıya varlığını sezdirmesin.
/// </summary>
public sealed record LeadBehavior(int Contacts, int Meetings, int RepeatInquiries, int InactiveDays = 0)
{
    public static LeadBehavior None { get; } = new(0, 0, 0);
}
