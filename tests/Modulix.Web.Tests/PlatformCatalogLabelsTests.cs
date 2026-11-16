using System.Globalization;
using System.Text.Json;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>
/// ADR-0034 (Platform P2): sektör ağacı (düğüm türü, hazırlık) ve rejim profili (sürüm durumu, ihlal düzeyi) tabloları ekrandan
/// <see cref="PlatformLabels"/>'a taşındı. Sözlük yüklenmemişken kaynak (Türkçe) metin gösterilir; testler her satırın üç sözlükte karşılığı
/// olduğunu, Türkçe sözlüğün kaynak metinle aynı kaldığını, eski ekran davranışının (bilinmeyen değerlerin gösterimi) korunduğunu ve
/// birleştirilmiş Türkçe metinlerin eski sabit metinle aynı çıktığını denetler.
/// </summary>
public class PlatformCatalogLabelsTests
{
    private static Dictionary<string, string> Dictionary(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Modulix.Core", "Localization", "Texts", "tr.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "src", "Modulix.Core", "Localization", "Texts", $"{language}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }

    // Sözlük testte yüklenmediği için tablolar kaynak metni döndürür.
    private static IEnumerable<(string Group, IReadOnlyList<(string Key, string Label)> Rows)> Tables() =>
    [
        (PlatformLabels.NodeKindGroup, PlatformLabels.NodeKinds),
        (PlatformLabels.NodeReadinessGroup, PlatformLabels.NodeReadinesses),
        (PlatformLabels.RegimeStateGroup, PlatformLabels.RegimeStates),
        (PlatformLabels.EnforcementGroup, PlatformLabels.Enforcements),
    ];

    private static IEnumerable<(string Key, string Label)> Rows() =>
        Tables().SelectMany(t => t.Rows.Select(r => (AdminLabels.KeyOf(t.Group, r.Key), r.Label)));

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    [InlineData("az")]
    public void Every_table_row_has_a_text_in_each_language(string language)
    {
        var texts = Dictionary(language);
        var missing = Rows()
            .Select(r => r.Key)
            .Where(k => !texts.TryGetValue(k, out var text) || string.IsNullOrWhiteSpace(text))
            .ToList();

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void Turkish_dictionary_matches_the_source_labels()
    {
        var texts = Dictionary("tr");
        var different = Rows()
            .Where(r => texts[r.Key] != r.Label)
            .Select(r => $"{r.Key}: {texts[r.Key]} != {r.Label}")
            .ToList();

        Assert.True(different.Count == 0, string.Join(Environment.NewLine, different));
    }

    [Fact]
    public void Tables_keep_the_values_the_server_expects()
    {
        // Seçeneklerin değerleri sunucuya gider (ActivityNodeDto.Kind/Readiness, RegistrationNoRuleDto.Enforcement); sıra ekrandaki sıradır.
        Assert.Equal(new[] { "Sector", "Group", "Activity" }, PlatformLabels.NodeKinds.Select(k => k.Key));
        Assert.Equal(new[] { "CatalogOnly", "Available" }, PlatformLabels.NodeReadinesses.Select(k => k.Key));
        Assert.Equal(new[] { "Draft", "Published", "Superseded" }, PlatformLabels.RegimeStates.Select(k => k.Key));
        Assert.Equal(new[] { "Warn", "Block" }, PlatformLabels.Enforcements.Select(k => k.Key));
    }

    [Fact]
    public void Lookups_keep_the_old_fallbacks()
    {
        Assert.Equal("Faaliyet grubu", PlatformLabels.NodeKindOf("Group"));
        Assert.Equal("Other", PlatformLabels.NodeKindOf("Other"));
        Assert.Equal("Hazır", PlatformLabels.NodeReadinessOf("Available"));
        Assert.Equal("Yalnızca katalog", PlatformLabels.NodeReadinessOf("CatalogOnly"));
        Assert.Equal("Yalnızca katalog", PlatformLabels.NodeReadinessOf("Other"));
        Assert.Equal("Taslak", PlatformLabels.RegimeStateOf("Draft"));
        Assert.Equal("Yayında", PlatformLabels.RegimeStateOf("Published"));
        Assert.Equal("Yerini bıraktı", PlatformLabels.RegimeStateOf("Superseded"));
        Assert.Equal("Yerini bıraktı", PlatformLabels.RegimeStateOf("Other"));
        Assert.Equal("platform.node-readiness.catalogonly", AdminLabels.KeyOf(PlatformLabels.NodeReadinessGroup, "CatalogOnly"));
    }

    [Fact]
    public void Turkish_dictionary_reproduces_the_composed_texts()
    {
        // Birleştirilmiş metinler Türkçe sözlükle eski sabit metnin verdiği sonucu üretmelidir.
        var texts = Dictionary("tr");
        string Format(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, texts[key], args);

        Assert.Equal("3 etkin, 1 pasif sektör", Format("platform.industries.counts", 3, 1));
        Assert.Equal("3 etkin, 1 pasif sektör · 2 sonuç", Format("platform.industries.countsFiltered", 3, 1, 2));
        Assert.Equal("\"Gıda\" pasife alındı; mevcut kayıtlardaki değer korunur.", Format("platform.industries.deactivated", "Gıda"));
        Assert.Equal("Yeni alt kayıt · Lojistik", Format("platform.sectors.newChild", "Lojistik"));
        Assert.Equal(
            "Gümrük tarife cetvelini Excel (.xlsx) ya da CSV olarak yükleyin. Dosyada <b>GTİP</b> (ya da Kod) ve <b>Tanım</b> başlıklı sütunlar gerekir; "
            + "<b>Birim</b> isteğe bağlıdır. Fasıl ve pozisyon başlıkları (6 haneden kısa kodlar) atlanır. Firmalar stok kartında yazdıkları kodu "
            + "yürürlükteki sürümle karşılaştırır; bulunamayan kod uyarıdır, engel değildir.",
            Format("platform.gtip.intro", "<b>GTİP</b>", "<b>Tanım</b>", "<b>Birim</b>"));
        Assert.Equal("“2026” yüklendi: 12 satır, 01.01.2026 tarihinden itibaren yürürlükte.", Format("platform.gtip.imported", "2026", 12, "01.01.2026"));
        Assert.Equal("Taslak (sürüm 2) kaydedildi.", Format("platform.regimeProfiles.draftSaved", 2));
        Assert.Equal("Sürüm 2 yayınlandı (doğrulanmadı). Önceki yayın geçmişte kaldı.", Format("platform.regimeProfiles.publishedUnverified", 2));
        Assert.Equal(
            "Her profil sürümlü bir kural paketidir. Firmalar profili seçince tescil numarası bu kurala göre denetlenir. Kuralı uzman doğrulamadan "
            + "<b>Engel</b> yapmayın: başlangıçta ihlal yalnızca uyarıdır ve yayın \"Doğrulanmadı\" olarak işaretlenir.",
            Format("platform.regimeProfiles.intro", $"<b>{texts["warehouse.regimeBindings.levelBlock"]}</b>"));
        Assert.Equal(@"ör. \d{8}[A-Z]{2}\d{8}", Format("platform.regimeProfiles.example", @"\d{8}[A-Z]{2}\d{8}"));
    }
}
