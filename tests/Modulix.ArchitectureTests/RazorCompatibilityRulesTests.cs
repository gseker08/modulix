using System.Text.RegularExpressions;

namespace Modulix.ArchitectureTests;

/// <summary>
/// <c>global.json</c> SDK 10.0.100 ister ve daha yeni bir banda kayabilir (<c>rollForward: latestFeature</c>); CI de 10.0.100 kurar. 10.0.1xx bandının
/// Razor derleyicisi iki yazımı yeni bantlardan farklı ayrıştırır, bu yüzden yeni bantta derlenen kod 10.0.1xx'te kırılır (16.11.2026'da beş dosya:
/// DashKpi, AppLauncher, LayoutForm, FormDesigner, ImportLog). Bu test iki deseni yasaklar:
/// <list type="bullet">
///   <item><c>@section.X</c>: <c>@section</c> Razor yönergesidir; <c>section</c> adlı değişkenin üyesine <c>@(section.X)</c> ile erişilir.</item>
///   <item>Satır başında ya da <c>{</c>, <c>,</c>, <c>(</c> ardından gelen <c>&lt;</c> ilişkisel deseni (<c>&lt; 0 =&gt;</c>): etiket sanılır;
///   karşılaştırma ifadesiyle (<c>x &lt; 0 ? … : …</c>) yazılır.</item>
/// </list>
/// </summary>
public sealed partial class RazorCompatibilityRulesTests
{
    [Fact]
    public void Razor_files_avoid_syntax_that_the_lowest_supported_sdk_misparses()
    {
        var web = new DirectoryInfo(Path.Combine(RepositoryLayout.Root.FullName, "src", "Modulix.Web"));
        var separator = Path.DirectorySeparatorChar;
        var violations = new List<string>();
        foreach (var file in web.EnumerateFiles("*.razor", SearchOption.AllDirectories)
                     .Where(f => !f.FullName.Contains($"{separator}obj{separator}") && !f.FullName.Contains($"{separator}bin{separator}")))
        {
            var lines = File.ReadAllLines(file.FullName);
            for (var i = 0; i < lines.Length; i++)
            {
                if (SectionMember().IsMatch(lines[i]))
                {
                    violations.Add($"{Path.GetRelativePath(web.FullName, file.FullName)}:{i + 1}: @section.… yerine @(section.…) yazın");
                }

                if (LessThanPattern().IsMatch(lines[i]))
                {
                    violations.Add($"{Path.GetRelativePath(web.FullName, file.FullName)}:{i + 1}: \"< … =>\" deseni yerine karşılaştırma ifadesi yazın");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [GeneratedRegex(@"@section\.")]
    private static partial Regex SectionMember();

    // "< 0 =>", "< 1024 * 1024 =>", "<= Limit =>": yalnızca değer ve aritmetik işleçlerden sonra "=>" gelir; HTML etiketi ("<div class=...>") eşleşmez.
    [GeneratedRegex(@"(?:^|[{,(])\s*<=?\s*-?[\w.]+(?:\s*[-+*/]\s*[\w.]+)*\s*=>")]
    private static partial Regex LessThanPattern();
}
