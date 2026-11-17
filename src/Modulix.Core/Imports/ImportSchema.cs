using Modulix.Core.Contracts.Customization;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Customization;
using Modulix.Core.Modules;
using Modulix.Core.Settings;
using Modulix.Core.Records;

namespace Modulix.Core.Imports;

/// <summary>
/// Kullanıcının bu firmada görebileceği içe aktarma sütunları: tanımın alanları, firmanın etkin özel alanları ve firmanın seçim listeleri.
/// Görme yetkisi olmayan hassas alan şablona girmez; dosyada varsa tanınmayan sütun gibi yok sayılır (ADR-0033 bölüm 7).
/// Kodda tanımlı adlar okurun dilindedir; eşleştirme öteki dillerdeki adları da kabul eder (<see cref="ImportLabels"/>, ADR-0034 dilim 18c-5).
/// </summary>
public sealed class ImportSchema(
    ICustomFieldProvider customFields, IFieldAccessService fieldAccess, IChoiceLists choiceLists, IImportDesignStore designs,
    ModuleCatalog catalog, IRequestLanguage requestLanguage, ITextCatalog texts)
{
    /// <summary>
    /// Şablon ve dosya sütunları: firmanın tasarımı uygulanmış hali. <paramref name="language"/> verilmezse isteğin dili; koşu, satır iletileri
    /// Türkçe olduğu sürece (dilim 18d) Türkçe etiketlerle çalışır.
    /// </summary>
    public async Task<IReadOnlyList<ImportField>> FieldsAsync(
        TenantContext tenant, ImportDefinition definition, CancellationToken cancellationToken, string? language = null) =>
        ImportDesignRules.Apply(
            await AllFieldsAsync(tenant, definition, cancellationToken, language), await designs.GetAsync(definition.EntityKey, cancellationToken));

    /// <summary>Tasarımdan önceki sütunlar (tasarım ekranı bunlardan seçer).</summary>
    public async Task<IReadOnlyList<ImportField>> AllFieldsAsync(
        TenantContext tenant, ImportDefinition definition, CancellationToken cancellationToken, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var bundle = texts.Bundle(language ?? await requestLanguage.GetAsync(cancellationToken));
        IReadOnlyList<IReadOnlyDictionary<string, string>> all = [.. LanguageOptions.Languages.Select(l => texts.Bundle(l.Value))];
        var hidden = await fieldAccess.GetHiddenFieldsAsync(tenant, definition.EntityKey, cancellationToken);
        var fields = new List<ImportField>();
        foreach (var field in definition.Fields.Where(f => !hidden.Contains(f.Key)))
        {
            var localized = ImportLabels.Localize(definition.EntityKey, field, bundle, all);
            if (field.ChoiceListKey is { } listKey)
            {
                // Firmanın listesi isteğin dilinde gelir (dilim 18c-4); hazır değerlerin Türkçe adı ve çevirileri eşleştirmede de kabul edilir.
                var active = (await choiceLists.GetAsync(listKey, cancellationToken)).Values.Where(v => v.Active).ToList();
                localized = localized with
                {
                    Options = [.. active.Select(v => new SettingOption(v.Key, v.Label))],
                    OptionVariants = catalog.FindChoiceList(listKey) is { } list
                        ? ImportLabels.ChoiceVariants(list.List, active.Where(v => v.BuiltIn).Select(v => v.Key), all)
                        : [],
                };
            }

            fields.Add(localized);
        }

        foreach (var custom in await customFields.GetFieldsAsync(tenant, definition.EntityKey, cancellationToken))
        {
            fields.Add(new ImportField(RecordFields.CustomPrefix + custom.Key, custom.Label, TypeOf(custom.Type), custom.Required)
            {
                Options = [.. custom.Options.Where(o => o.Active).Select(o => new SettingOption(o.Key, o.Label))],
            });
        }

        return fields;
    }

    private static ImportFieldType TypeOf(CustomFieldType type) => type switch
    {
        CustomFieldType.Number => ImportFieldType.Decimal,
        CustomFieldType.Date => ImportFieldType.Date,
        CustomFieldType.YesNo => ImportFieldType.YesNo,
        CustomFieldType.Choice => ImportFieldType.Choice,
        CustomFieldType.MultiChoice => ImportFieldType.MultiChoice,
        _ => ImportFieldType.Text,
    };
}
