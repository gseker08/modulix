using System.Text.Json;
using System.Text.RegularExpressions;

namespace Modulix.ArchitectureTests;

/// <summary>
/// ADR-0034 test kapısı: dil sözlükleri birbiriyle uyumlu kalır ve koddaki her metin anahtarı Türkçe (kaynak dil) sözlükte
/// bulunur. Yeni bir metin eklenirken bir dilin unutulması ya da anahtarın yanlış yazılması derlemede değil burada yakalanır.
/// </summary>
public partial class LocalizationRulesTests
{
    private static readonly string[] Languages = ["tr", "en", "az"];

    private static string TextsDirectory => Path.Combine(
        RepositoryLayout.SourceProject("Modulix.Core").Directory!.FullName, "Localization", "Texts");

    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TextsDirectory, language + ".json")))!;

    [Fact]
    public void Every_supported_language_has_a_dictionary_and_no_other_files_exist()
    {
        var files = Directory.GetFiles(TextsDirectory, "*.json").Select(Path.GetFileNameWithoutExtension).Order().ToList();

        Assert.Equal(Languages.Order(), files);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("az")]
    public void Dictionaries_have_exactly_the_keys_of_the_source_language(string language)
    {
        var source = Load("tr").Keys.ToHashSet();
        var other = Load(language).Keys.ToHashSet();

        Assert.True(source.IsSubsetOf(other), $"{language}.json'da eksik anahtarlar: {string.Join(", ", source.Except(other).Order())}");
        Assert.True(other.IsSubsetOf(source), $"{language}.json'da tr.json'da olmayan anahtarlar: {string.Join(", ", other.Except(source).Order())}");
    }

    /// <summary>
    /// JSON okuyucusu tekrarlanan anahtarda son değeri sessizce tutar; bu yüzden sözlük ham olarak taranır ve aynı anahtar
    /// ikinci kez yazılmışsa test kırılır (birinin metni fark edilmeden ötekini ezmesin).
    /// </summary>
    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    [InlineData("az")]
    public void Dictionaries_do_not_contain_duplicate_keys(string language)
    {
        byte[] json = File.ReadAllBytes(Path.Combine(TextsDirectory, language + ".json"));
        Utf8JsonReader reader = new(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        HashSet<string> seen = [];
        List<string> duplicates = [];

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
            {
                string key = reader.GetString()!;
                if (!seen.Add(key))
                {
                    duplicates.Add(key);
                }
            }
        }

        Assert.True(duplicates.Count == 0, $"{language}.json'da birden çok kez geçen anahtarlar: {string.Join(", ", duplicates.Distinct().Order())}");
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    [InlineData("az")]
    public void Keys_are_well_formed_and_texts_are_not_empty(string language)
    {
        foreach (var (key, text) in Load(language))
        {
            Assert.Matches(KeyPattern(), key);
            Assert.False(string.IsNullOrWhiteSpace(text), $"{language}.json: '{key}' boş.");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("az")]
    public void Placeholders_match_the_source_language(string language)
    {
        var source = Load("tr");
        foreach (var (key, text) in Load(language).Where(t => source.ContainsKey(t.Key)))
        {
            Assert.Equal(Placeholders(source[key]), Placeholders(text));
        }
    }

    [Fact]
    public void Every_text_key_used_in_the_web_project_exists_in_the_source_dictionary()
    {
        var known = Load("tr").Keys.ToHashSet();
        var web = RepositoryLayout.SourceProject("Modulix.Web").Directory!;
        var missing = new List<string>();

        foreach (var file in web.EnumerateFiles("*.*", SearchOption.AllDirectories)
                     .Where(f => f.Extension is ".razor" or ".cs" && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match match in UsedKey().Matches(File.ReadAllText(file.FullName)))
            {
                if (!known.Contains(match.Groups["key"].Value))
                {
                    missing.Add($"{file.Name}: {match.Groups["key"].Value}");
                }
            }
        }

        Assert.True(missing.Count == 0, "tr.json'da olmayan metin anahtarları:\n" + string.Join("\n", missing));
    }

    private static string Placeholders(string text) =>
        string.Join(",", PlaceholderPattern().Matches(text).Select(m => m.Value).Distinct().Order());

    [GeneratedRegex(@"^[a-z][a-z0-9-]*(\.[A-Za-z0-9-]+)+$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"Texts\.(?:Get|Strong)\(\s*""(?<key>[^""]+)""")]
    private static partial Regex UsedKey();
}
