using System.Text.Json;
using Modulix.Shared.Hr;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>
/// ADR-0034 aşama 4, İK dilim 19a (blog ve galeri) 19b (duyurular) 19c (anketler) 19d (izinler) 19e (masraflar) ve 19f (çalışanlar): blog durumları, yayın durumu, hedef kitle "Herkes", anket durumları, soru türleri, izin, masraf ve çalışan durumları, çalışma şekilleri ve gün biçimi sözlükten gelir; Türkçe kaynak metin (koddaki yedek)
/// <c>tr.json</c> ile aynıdır. Diller arası anahtar eşitliğini ve kullanılan anahtarların varlığını mimari testler denetler.
/// </summary>
public class HrTextTests
{
    private static readonly string[] BlogStatuses = ["draft", "submitted", "returned", "published"];

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
    public void Blog_statuses_and_publication_have_a_turkish_text_equal_to_the_source_label()
    {
        var tr = Turkish();
        foreach (var status in BlogStatuses)
        {
            Assert.Equal(HrAccess.BlogStatus(status).Label, tr[$"hr.blog-status.{status}"]);
        }

        Assert.Equal("Yayında", tr["hr.content.published"]);
        Assert.Equal("Herkes", tr["hr.audience.everyone"]);
        Assert.Equal("Taslak", tr["hr.content.draft"]);
    }

    [Fact]
    public void Survey_statuses_and_question_types_have_a_turkish_text_equal_to_the_source_label()
    {
        var tr = Turkish();
        foreach (var (key, label) in HrAccess.SurveyStatuses)
        {
            Assert.Equal(label, tr[WarehouseLabels.KeyOf("hr.survey-status", key)]);
        }

        foreach (var (key, label) in HrAccess.QuestionTypes)
        {
            Assert.Equal(label, tr[WarehouseLabels.KeyOf("hr.question-type", key)]);
        }

        foreach (var (key, label) in HrAccess.LeaveStatuses)
        {
            Assert.Equal(label, tr[WarehouseLabels.KeyOf("hr.leave-status", key)]);
        }

        Assert.Equal("{0} gün", tr["hr.leave.days"]);

        foreach (var (key, label) in HrAccess.ExpenseStatuses)
        {
            Assert.Equal(label, tr[WarehouseLabels.KeyOf("hr.expense-status", key)]);
        }

        foreach (var (key, label) in HrAccess.Statuses)
        {
            Assert.Equal(label, tr[WarehouseLabels.KeyOf("hr.employee-status", key)]);
        }

        foreach (var (key, label) in HrAccess.EmploymentTypes)
        {
            Assert.Equal(label, tr[WarehouseLabels.KeyOf("hr.employment-type", key)]);
        }
    }

    [Fact]
    public void Without_a_loaded_dictionary_the_turkish_source_label_and_the_same_badge_are_shown()
    {
        foreach (var status in BlogStatuses)
        {
            Assert.Equal(HrAccess.BlogStatus(status), HrLabels.BlogStatus(status));
        }

        Assert.Equal(("Taslak", "status-inactive"), HrLabels.BlogStatus(null));
        Assert.Equal(("Yayında", "status-customer"), HrLabels.Published(true));
        Assert.Equal(("Taslak", "status-inactive"), HrLabels.Published(false));
        Assert.Equal("Herkes", HrLabels.Everyone);
        Assert.Equal(HrAccess.SurveyStatuses, HrLabels.SurveyStatuses);
        Assert.Equal("Çoklu seçim", HrLabels.QuestionType("multiChoice"));
        Assert.Equal("İptal edildi", HrLabels.LeaveStatus("cancelled"));
        Assert.Equal("Ödendi", HrLabels.ExpenseStatus("paid"));
        Assert.Equal("İzinde", HrLabels.Status("onLeave"));
        Assert.Equal("Belirli süreli", HrLabels.EmploymentType("fixedTerm"));
        Assert.Equal("Unvan girilmedi", HrLabels.NoJobTitle);
        Assert.Equal("1,5 gün", HrLabels.Days(1.5m));
        Assert.Equal("—", HrLabels.Days(null));
    }
}
