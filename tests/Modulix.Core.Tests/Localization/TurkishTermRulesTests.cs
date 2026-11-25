using System.Text.RegularExpressions;
using Modulix.Core.Localization;

namespace Modulix.Core.Tests.Localization;

/// <summary>
/// CLAUDE.md terim kuralı: Türkçe arayüzde "Lead" yazılmaz, "Müşteri adayı" yazılır (menü, başlık, düğme, ileti, izin adı, alan adı).
/// Teknik adlar kodda ve API'de kalır; bu test yalnızca Türkçe sözlük değerlerine bakar. Sözlükteki "Lead" geçen tek meşru değer, API'nin kabul
/// ettiği hedef türü değerlerini sayan iletidir (değerler teknik, çevrilemez); yeni bir istisna eklenmez, önce metin "Müşteri adayı" yazılır.
/// İngilizce ve Azerice sözlükler bu kuralın dışındadır ("Lead" orada doğru terimdir).
/// </summary>
public sealed partial class TurkishTermRulesTests
{
    /// <summary>Anahtar → neden: API'nin geçerli değerlerini aynen sayan ileti.</summary>
    private static readonly Dictionary<string, string> TechnicalValues = new()
    {
        ["marketing.server.endpoint.invalid-target"] = "Geçerli hedef türü değerleri (Company, Person, Lead) API değerleridir, çevrilmez.",
    };

    [Fact]
    public void Turkish_dictionary_values_do_not_say_Lead_except_listed_technical_values()
    {
        var turkish = new TextCatalog().Bundle("tr");

        var violations = turkish
            .Where(p => LeadWord().IsMatch(p.Value) && !TechnicalValues.ContainsKey(p.Key))
            .Select(p => $"{p.Key}: {p.Value}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Türkçe sözlükte 'Lead' geçiyor; arayüzde 'Müşteri adayı' yazılır (CLAUDE.md terim kuralı):" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Listed_technical_values_still_exist_and_still_need_the_exception()
    {
        var turkish = new TextCatalog().Bundle("tr");

        foreach (var (key, _) in TechnicalValues)
        {
            Assert.True(turkish.TryGetValue(key, out var value), $"{key} sözlükte yok; istisnayı silin.");
            Assert.True(LeadWord().IsMatch(value!), $"{key} artık 'Lead' içermiyor; istisnayı silin.");
        }
    }

    [Theory]
    [InlineData("Lead oluşturabilir", true)]
    [InlineData("Teklif ve lead kohortları", true)]
    [InlineData("Kaynağa göre lead'ler", true)]
    [InlineData("Lead no", true)]
    [InlineData("Dönüşen Lead.", true)]
    [InlineData("{{crm.lead.subject}} yazın", false)]
    [InlineData("Müşteri adayı oluşturabilir", false)]
    [InlineData("Leadership", false)]
    [InlineData("crm.lead.subject", false)]
    public void The_pattern_matches_the_word_and_not_technical_names_or_longer_words(string text, bool expected) =>
        Assert.Equal(expected, LeadWord().IsMatch(text));

    // "Lead" ya da "lead" tek başına sözcük olarak ("lead'ler" ve cümle sonu "Lead." dahil); "Leadership" ve "crm.lead.subject" gibi uzun/noktalı teknik adlar eşleşmez.
    [GeneratedRegex(@"(?<![\w.])[Ll]ead(?!\w|\.\w)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadWord();
}
