namespace Modulix.Shared.Crm;

/// <summary>
/// Müşteri adayı puan kuralı ekranının alan ve işleç kataloğu (ADR-0050). Sunucudaki beyaz listenin (<c>LeadScoring.Fields</c>) arayüz
/// kopyasıdır; ikisinin aynı kaldığını entegrasyon testi denetler. Kurallar genel kayıt uçlarındadır: <c>/api/records/crm.lead-score-rule</c>.
/// </summary>
public static class LeadScoreCatalog
{
    public const string EntityKey = "crm.lead-score-rule";

    public const string EqualsTo = "equals";
    public const string NotEquals = "notEquals";
    public const string Contains = "contains";
    public const string IsSet = "isSet";
    public const string IsEmpty = "isEmpty";

    /// <summary>Davranış sayacı (dilim 2): en az N; değer 1..1000 tam sayı.</summary>
    public const string AtLeast = "atLeast";

    public sealed record ScoreFieldOption(string Key, string Label, IReadOnlyList<string> Operators);

    private static readonly string[] CodeOperators = [EqualsTo, NotEquals, IsSet, IsEmpty];
    private static readonly string[] TextOperators = [EqualsTo, NotEquals, Contains, IsSet, IsEmpty];
    private static readonly string[] CodeListOperators = [Contains, IsSet, IsEmpty];
    private static readonly string[] PresenceOperators = [IsSet, IsEmpty];
    private static readonly string[] CountOperators = [AtLeast];

    public static readonly IReadOnlyList<ScoreFieldOption> Fields =
    [
        new("source", "Kaynak", CodeOperators),
        new("industry", "Sektör", CodeOperators),
        new("country", "Ülke", CodeOperators),
        new("city", "Şehir", TextOperators),
        new("priority", "Öncelik", CodeOperators),
        new("jobTitle", "Unvan", TextOperators),
        new("companyName", "Firma adı", TextOperators),
        new("productInterest", "İlgilenilen ürün/hizmet", TextOperators),
        new("serviceScope", "Hizmet kapsamı", CodeListOperators),
        new("utmSource", "UTM kaynak", CodeOperators),
        new("utmMedium", "UTM ortam", CodeOperators),
        new("utmCampaign", "UTM kampanya", CodeOperators),
        new("email", "E-posta", PresenceOperators),
        new("phone", "Telefon", PresenceOperators),
        new("website", "Web sitesi", PresenceOperators),
        new("companyId", "Mevcut firma", PresenceOperators),
        new("campaignId", "Kampanya", PresenceOperators),
        new("contacts", "Gerçekleşen temas sayısı", CountOperators),
        new("meetings", "Yapılan toplantı sayısı", CountOperators),
        new("repeatInquiries", "Tekrar başvuru sayısı", CountOperators),
        new("inactiveDays", "Hareketsiz gün sayısı", CountOperators),
    ];

    public static ScoreFieldOption? Find(string? key) => Fields.FirstOrDefault(f => f.Key == key);

    public static string OperatorLabel(string op) => op switch
    {
        EqualsTo => "eşittir",
        NotEquals => "eşit değildir",
        Contains => "içerir",
        IsSet => "dolu",
        IsEmpty => "boş",
        AtLeast => "en az",
        _ => op,
    };

    public static bool NeedsValue(string op) => op is not (IsSet or IsEmpty);

    /// <summary>Kuralın koşulu okunur metin olarak (ör. <c>Ülke eşittir "TR"</c>, <c>Hareketsiz gün sayısı en az 30</c>).</summary>
    public static string ConditionText(string field, string op, string? value)
    {
        var label = Find(field)?.Label ?? field;
        return !NeedsValue(op)
            ? $"{label} {OperatorLabel(op)}"
            : op == AtLeast ? $"{label} {OperatorLabel(op)} {value}" : $"{label} {OperatorLabel(op)} \"{value}\"";
    }

    /// <summary>Puan işaretli yazılır: +20, -15.</summary>
    public static string PointsText(int points) =>
        points > 0 ? "+" + points.ToString(System.Globalization.CultureInfo.InvariantCulture) : points.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Müşteri adayı puan açıklaması (ADR-0050 dilim 4; <c>GET /api/crm/leads/{id}/score</c>). <see cref="Score"/> kayıttaki puan,
/// <see cref="Calculated"/> bugünkü kurallarla hesaplanan puandır; farklıysa puan güncellenmeyi bekliyor ya da aday kapalı olduğu için donmuştur.
/// <see cref="Reasons"/> puana katkı veren etkin kurallardır (toplamları 0..100'e kırpılmadan önceki değerdir).
/// </summary>
public sealed record LeadScoreExplanationDto(int Score, int Calculated, IReadOnlyList<LeadScoreReasonDto> Reasons);

/// <summary>Puana katkı veren tek kural: adı, koşulu (alan, işleç, değer; <see cref="LeadScoreCatalog"/> ile okunur) ve puanı.</summary>
public sealed record LeadScoreReasonDto(Guid RuleId, string Name, string Field, string Operator, string? Value, int Points);
