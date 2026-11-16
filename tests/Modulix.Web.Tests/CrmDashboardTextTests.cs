using System.Text.Json;

namespace Modulix.Web.Tests;

/// <summary>
/// ADR-0034 aşama 4, dilim 20 (CRM panosu ve Benim günüm): parçalardan kurulan cümleler (öneri kuralları, selamlama, tempo) Türkçe sözlükle
/// eski sabit metinle aynı çıkar. Diller arası anahtar eşitliğini ve kullanılan anahtarların varlığını mimari testler denetler.
/// </summary>
public class CrmDashboardTextTests
{
    private static Dictionary<string, string> Turkish()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Modulix.Core", "Localization", "Texts", "tr.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(directory!.FullName, "src", "Modulix.Core", "Localization", "Texts", "tr.json")))!;
    }

    [Fact]
    public void The_rules_hint_built_from_the_turkish_dictionary_reads_as_before()
    {
        var tr = Turkish();
        List<string> rules =
        [
            tr["crm.my-day.rule.accepted"], tr["crm.my-day.rule.new-lead"], tr["crm.my-day.rule.overdue"],
            string.Format(tr["crm.my-day.rule.quotes"], 7), string.Format(tr["crm.my-day.rule.stale"], 14),
        ];
        var list = string.Join(", ", rules.GetRange(0, rules.Count - 1)) + " " + tr["crm.my-day.and"] + " " + rules[^1];

        Assert.Equal(
            "Öneriler kurallarla üretilir (yapay zekâ değil): teklifi kabul edilen açık fırsatlar, ilk teması bekleyen müşteri adayları, kapanış tarihi geçen fırsatlar, "
            + "7 gündür yanıtsız teklifler ve 14 gündür temas olmayan fırsatlar. Süreler firma ayarlarından değiştirilir.",
            string.Format(tr["crm.my-day.rules"], list) + " " + tr["crm.my-day.rules-settings"]);
    }

    [Fact]
    public void Dashboard_sentences_keep_their_turkish_wording()
    {
        var tr = Turkish();

        Assert.Equal("Hedefin %40 gerçekleşti, dönemin %55 geçti", string.Format(tr["crm.dashboard.meter-tempo"], "%40", "%55"));
        Assert.Equal("Tempo %55 · 15 puan geride", string.Format(tr["crm.my-day.tempo-behind"], "%55", "15"));
        Assert.Equal("Bugün 3 işiniz var", string.Format(tr["crm.dashboard.today-count"], 3));
        Assert.Equal("En uzun bekleyen aşama: Teklif (ort. 4,5 gün)", string.Format(tr["crm.dashboard.slowest"], "Teklif", "4,5"));
    }
}
