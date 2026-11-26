namespace Modulix.ArchitectureTests;

/// <summary>
/// Arayüz tasarım kuralı (CLAUDE.md, düğmeler): kaydetme sırasında düğmeye <c>aria-busy="true"</c> verilir; ekran okuyucu ve çift tıklama
/// kullanıcısı işlemin sürdüğünü bu işaretten anlar. Kural <c>type="submit"</c> olan birincil (<c>btn-primary</c>) düğmeler için denetlenir;
/// istisna yoktur. Yeni düğme <c>aria-busy="@(saving ? "true" : "false")"</c> taşır (değişken düğmenin <c>disabled</c> ifadesindekidir).
/// </summary>
public sealed class ButtonBusyRulesTests
{
    [Fact]
    public void Primary_submit_buttons_declare_aria_busy()
    {
        var violations = CountMissing().Select(p => $"{p.File}: {p.Missing}").ToList();

        Assert.True(
            violations.Count == 0,
            "aria-busy'siz birincil gönder düğmesi var:" + Environment.NewLine + string.Join(Environment.NewLine, violations)
            + Environment.NewLine + "Düğmeye aria-busy=\"@(busy ? \"true\" : \"false\")\" ekleyin.");
    }

    [Theory]
    [InlineData("<button type=\"submit\" class=\"btn btn-primary\">Kaydet</button>", 1)]
    [InlineData("<button class=\"btn btn-primary\" type=\"submit\" aria-busy=\"@(busy ? \"true\" : \"false\")\">Kaydet</button>", 0)]
    [InlineData("<button type=\"submit\" class=\"btn btn-primary\" disabled=\"@(a > b)\" @onclick=\"() => Save()\">Kaydet</button>", 1)]
    [InlineData("<button type=\"submit\" class=\"btn btn-primary\" @onclick=\"() => Save()\" aria-busy=\"@(busy ? \"true\" : \"false\")\">Kaydet</button>", 0)]
    [InlineData("<button type=\"button\" class=\"btn btn-primary\">Aç</button>", 0)]
    [InlineData("<button type=\"submit\" class=\"btn btn-outline-secondary\">Önizle</button>", 0)]
    [InlineData("<a class=\"btn btn-primary\" href=\"x\">Git</a>", 0)]
    public void The_scanner_reads_tags_with_arrows_nested_quotes_and_any_attribute_order(string markup, int expected)
    {
        Assert.Equal(expected, ButtonTags.CountPrimarySubmitWithoutBusy(markup));
    }

    private static IEnumerable<(string File, int Missing)> CountMissing()
    {
        var web = new DirectoryInfo(Path.Combine(RepositoryLayout.Root.FullName, "src", "Modulix.Web"));
        var separator = Path.DirectorySeparatorChar;
        foreach (var file in web.EnumerateFiles("*.razor", SearchOption.AllDirectories)
                     .Where(f => !f.FullName.Contains($"{separator}obj{separator}") && !f.FullName.Contains($"{separator}bin{separator}")))
        {
            var missing = ButtonTags.CountPrimarySubmitWithoutBusy(File.ReadAllText(file.FullName));
            if (missing > 0)
            {
                yield return (Path.GetRelativePath(web.FullName, file.FullName).Replace('\\', '/'), missing);
            }
        }
    }
}

/// <summary>
/// Razor işaretlemesinden <c>&lt;button …&gt;</c> açılış etiketlerini okur. Etiket sonu (<c>&gt;</c>) tırnak içinde ve <c>@( … )</c> ifadesi içinde sayılmaz:
/// <c>@onclick="() =&gt; Kaydet()"</c> ve <c>aria-busy="@(busy ? "true" : "false")"</c> gibi öznitelikler etiketi erken bitirmez.
/// </summary>
internal static class ButtonTags
{
    public static int CountPrimarySubmitWithoutBusy(string markup)
    {
        var count = 0;
        foreach (var tag in OpeningTags(markup))
        {
            if (tag.Contains("type=\"submit\"", StringComparison.Ordinal)
                && tag.Contains("btn-primary", StringComparison.Ordinal)
                && !tag.Contains("aria-busy", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<string> OpeningTags(string markup)
    {
        const string open = "<button";
        var index = 0;
        while ((index = markup.IndexOf(open, index, StringComparison.Ordinal)) >= 0)
        {
            var after = index + open.Length;
            if (after >= markup.Length || !(char.IsWhiteSpace(markup[after]) || markup[after] == '>'))
            {
                index = after;
                continue;
            }

            var end = TagEnd(markup, after);
            yield return markup.Substring(index, end - index);
            index = end;
        }
    }

    private static int TagEnd(string text, int start)
    {
        var inQuote = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuote && c == '@' && i + 1 < text.Length && text[i + 1] == '(')
            {
                i = SkipParentheses(text, i + 1);
                continue;
            }

            if (c == '"')
            {
                inQuote = !inQuote;
            }
            else if (c == '>' && !inQuote)
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    // "@(" ifadesinin kapanış parantezini bulur (içindeki tırnak ve parantezler dikkate alınmaz); döndürdüğü indeks kapanış parantezidir.
    private static int SkipParentheses(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return i;
            }
        }

        return text.Length - 1;
    }
}
