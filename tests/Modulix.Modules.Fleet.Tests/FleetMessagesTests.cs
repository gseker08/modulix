using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Modulix.Core.Contracts.Localization;
using Modulix.Modules.Fleet;


namespace Modulix.Modules.Fleet.Tests;

/// <summary>
/// ADR-0034 §6, dilim 18d-4: Filo modülünün sunucu iletileri (<c>fleet.server.*</c>). Test kapısı: her iletinin anahtarı üç sözlükte var,
/// Türkçe metni <c>tr.json</c> kalıbıyla birebir aynı, çevirilerin yer tutucuları Türkçeyle aynı ve sözlükte kullanılmayan
/// <c>fleet.server.*</c> anahtarı yok.
/// </summary>
public partial class FleetMessagesTests
{
    private const string Prefix = "fleet.server.";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Dictionaries = new(LoadDictionaries);

    [Fact]
    public void Every_fleet_message_matches_the_turkish_dictionary()
    {
        var turkish = Dictionaries.Value["tr"];
        var checkedCount = 0;
        foreach (var (name, message) in AllMessages())
        {
            Assert.NotNull(message.Key);
            Assert.True(message.Key!.StartsWith(Prefix, StringComparison.Ordinal), $"{name}: '{message.Key}' {Prefix} ile başlamalı.");
            Assert.Matches(KeyPattern(), message.Key);
            Assert.True(turkish.TryGetValue(message.Key, out var pattern), $"{name}: '{message.Key}' tr.json'da yok.");
            Assert.Equal(message.Args.Count == 0 ? pattern : string.Format(CultureInfo.InvariantCulture, pattern!, [.. message.Args]), message.Text);
            checkedCount++;
        }

        Assert.True(checkedCount >= 13);
    }

    [Fact]
    public void Every_fleet_server_key_is_used_and_translated_with_the_same_placeholders()
    {
        var used = AllMessages().Select(m => m.Message.Key).ToHashSet();
        var keys = Dictionaries.Value["tr"].Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToList();

        var unused = keys.Where(k => !used.Contains(k)).ToList();
        Assert.True(unused.Count == 0, "Kullanılmayan destek sunucu anahtarları: " + string.Join(", ", unused));

        foreach (var language in new[] { "en", "az" })
        {
            var bundle = Dictionaries.Value[language];
            var missing = keys.Where(k => !bundle.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{language}.json'da eksik: " + string.Join(", ", missing));
            var mismatched = keys.Where(k => !Placeholders(bundle[k]).SetEquals(Placeholders(Dictionaries.Value["tr"][k]))).ToList();
            Assert.True(mismatched.Count == 0, $"{language}.json'da yer tutucusu uymayan: " + string.Join(", ", mismatched));
        }
    }

    private static HashSet<string> Placeholders(string text) => [.. PlaceholderPattern().Matches(text).Select(m => m.Value)];

    private static IEnumerable<(string Name, ServerMessage Message)> AllMessages()
    {
        foreach (var group in typeof(FleetMessages).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var property in group.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(ServerMessage)))
            {
                yield return ($"{group.Name}.{property.Name}", (ServerMessage)property.GetValue(null)!);
            }

            foreach (var method in group.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(ServerMessage) && !m.IsSpecialName))
            {
                var args = method.GetParameters().Select(p => p.ParameterType == typeof(string) ? "x" + p.Position : Convert.ChangeType(7, p.ParameterType, CultureInfo.InvariantCulture)).ToArray();
                yield return ($"{group.Name}.{method.Name}", (ServerMessage)method.Invoke(null, args)!);
            }
        }
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadDictionaries()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(TextsPath(directory, "tr")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return new[] { "tr", "en", "az" }.ToDictionary(
            language => language,
            language => (IReadOnlyDictionary<string, string>)JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(TextsPath(directory!, language)))!);
    }

    private static string TextsPath(DirectoryInfo root, string language) =>
        Path.Combine(root.FullName, "src", "Modulix.Core", "Localization", "Texts", language + ".json");

    [GeneratedRegex(@"^[a-z][a-z0-9-]*(\.[A-Za-z0-9-]+)+$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
