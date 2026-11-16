using System.Text.Json;
using Modulix.Shared.Hr;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>
/// ADR-0034 aşama 4, İK dilim 19a (blog ve galeri) ve 19b (duyurular): blog durumları, yayın durumu ve hedef kitle "Herkes" sözlükten gelir; Türkçe kaynak metin (koddaki yedek)
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
    }
}
