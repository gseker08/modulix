using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Modules;

namespace Modulix.Core.Imports;

/// <summary>
/// İçe aktarma tanımının adları okurun dilinde (ADR-0034, dilim 18c-5): tanım adı, sütun etiketi ve kodda tanımlı sabit seçenekler. Anahtarlar
/// <see cref="MetadataTexts"/> ile tanımlayıcılardan türer; sözlükte yoksa Türkçe kaynak metin. Şablon başlığı okurun dilinde olsa da yüklenen
/// dosya eşleştirmede Türkçe kaynak adı ve desteklenen bütün dillerdeki çevirileri de kabul eder (<see cref="ImportField.LabelVariants"/>,
/// <see cref="ImportField.OptionVariants"/>): Türkçe şablonu dolduran ekip arkadaşı da, İngilizce şablonu dolduran da aynı dosyayı yükleyebilir.
/// </summary>
public static class ImportLabels
{
    /// <summary>Tanımın adı <paramref name="bundle"/> dilinde.</summary>
    public static string Name(ImportDefinition definition, IReadOnlyDictionary<string, string> bundle)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return MetadataTexts.Text(bundle, MetadataTexts.ImportKey(definition.EntityKey), definition.DisplayName);
    }

    /// <summary>
    /// Kodda tanımlı sütun <paramref name="bundle"/> dilinde; öteki dillerdeki etiket ve sabit seçenek adları eşleştirme için eklenir.
    /// <paramref name="all"/> desteklenen dillerin sözlükleridir.
    /// </summary>
    public static ImportField Localize(
        string entityKey, ImportField field, IReadOnlyDictionary<string, string> bundle, IReadOnlyList<IReadOnlyDictionary<string, string>> all)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(all);
        var labelKey = MetadataTexts.ImportFieldKey(entityKey, field.Key);
        var label = MetadataTexts.Text(bundle, labelKey, field.Label);
        return field with
        {
            Label = label,
            LabelVariants = Variants(field.Label, labelKey, all, label),
            Options = [.. field.Options.Select(o => o with { Label = MetadataTexts.Text(bundle, MetadataTexts.ImportOptionKey(entityKey, field.Key, o.Value), o.Label) })],
            OptionVariants = [.. field.Options.SelectMany(o =>
                Variants(o.Label, MetadataTexts.ImportOptionKey(entityKey, field.Key, o.Value), all, null).Select(name => new SettingOption(o.Value, name)))],
        };
    }

    /// <summary>
    /// Seçim listesine bağlı sütunda kodda tanımlı hazır değerlerin Türkçe adı ve çevirileri (eşleştirme için). Seçenekler firmanın listesinden
    /// okurun dilinde gelir; firmanın eklediği değerlerin tek adı vardır.
    /// </summary>
    public static IReadOnlyList<SettingOption> ChoiceVariants(
        ChoiceListDefinition list, IEnumerable<string> activeKeys, IReadOnlyList<IReadOnlyDictionary<string, string>> all)
    {
        ArgumentNullException.ThrowIfNull(list);
        var active = activeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            .. list.Values.Where(v => active.Contains(v.Key)).SelectMany(v =>
                Variants(v.Label, MetadataTexts.ChoiceValueKey(list.Key, v.Key), all, null).Select(name => new SettingOption(v.Key, name))),
        ];
    }

    /// <summary>Türkçe kaynak ad ve her dildeki çevirisi; büyük/küçük harf duyarsız tekrarsız, <paramref name="except"/> hariç.</summary>
    private static IReadOnlyList<string> Variants(
        string source, string key, IReadOnlyList<IReadOnlyDictionary<string, string>> all, string? except) =>
    [
        .. all.Select(b => MetadataTexts.Text(b, key, source))
            .Prepend(source)
            .Where(n => except is null || ImportColumns.Name(n) != ImportColumns.Name(except))
            .DistinctBy(ImportColumns.Name, StringComparer.Ordinal),
    ];
}
