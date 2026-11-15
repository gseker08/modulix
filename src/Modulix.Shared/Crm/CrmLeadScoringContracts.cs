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

    public sealed record ScoreFieldOption(string Key, string Label, IReadOnlyList<string> Operators);

    private static readonly string[] CodeOperators = [EqualsTo, NotEquals, IsSet, IsEmpty];
    private static readonly string[] TextOperators = [EqualsTo, NotEquals, Contains, IsSet, IsEmpty];
    private static readonly string[] CodeListOperators = [Contains, IsSet, IsEmpty];
    private static readonly string[] PresenceOperators = [IsSet, IsEmpty];

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
    ];

    public static ScoreFieldOption? Find(string? key) => Fields.FirstOrDefault(f => f.Key == key);

    public static string OperatorLabel(string op) => op switch
    {
        EqualsTo => "eşittir",
        NotEquals => "eşit değildir",
        Contains => "içerir",
        IsSet => "dolu",
        IsEmpty => "boş",
        _ => op,
    };

    public static bool NeedsValue(string op) => op is not (IsSet or IsEmpty);
}
