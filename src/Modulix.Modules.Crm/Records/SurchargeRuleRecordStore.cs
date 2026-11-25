using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Pricing;

namespace Modulix.Modules.Crm.Records;

/// <summary>
/// Ek ücret kuralı kayıt deposu (ADR-0018 devamı, Dilim 2b). Okuma <c>crm.prices.read</c>, yazma <c>crm.prices.write</c> iznindedir; kural tüm
/// tenant'a uygulanır (veri kapsamı yok) ve teklif hesabında sunucu tarafında iç okuyucuyla okunur. Alan kuralları kural türüne göre denetlenir
/// (<see cref="SurchargeRules.Validate"/>); hesabı <c>SurchargeEngine</c> yapar.
/// </summary>
internal sealed class SurchargeRuleRecordStore(CrmStoreContext context) : CrmRecordStore<SurchargeRule>(context)
{
    public override string EntityKey => CrmModule.Entities.SurchargeRule;

    protected override string ReadPermission => CrmModule.Permissions.PricesRead;

    protected override string WritePermission => CrmModule.Permissions.PricesWrite;

    protected override bool DefaultsOwnerToCurrentUser => false;

    protected override IReadOnlyList<RecordField<SurchargeRule>> Fields { get; } =
    [
        Field.Text<SurchargeRule>("name", SurchargeRule.NameMaxLength, r => r.Name, (r, v) => r.Name = v!, required: true),
        Field.Text<SurchargeRule>("description", SurchargeRule.DescriptionMaxLength, r => r.Description, (r, v) => r.Description = v),
        Field.Count<SurchargeRule>("priority", r => r.Priority, (r, v) => r.Priority = v),
        Field.Flag<SurchargeRule>("isActive", r => r.IsActive, (r, v) => r.IsActive = v),
        Field.Choice<SurchargeRule>("kind", SurchargeRules.Kinds, r => r.Kind, (r, v) => r.Kind = v),
        Field.Currency<SurchargeRule>("currency", r => r.Currency, (r, v) => r.Currency = v),
        Field.Money<SurchargeRule>("amount", r => r.Amount, (r, v) => r.Amount = v),
        Field.Rate<SurchargeRule>("percent", r => r.Percent, (r, v) => r.Percent = v),
        Field.Rate<SurchargeRule>("taxRatePercent", r => r.TaxRatePercent, (r, v) => r.TaxRatePercent = v),
        Field.Text<SurchargeRule>("group", SurchargeRule.GroupMaxLength, r => r.GroupKey, (r, v) => r.GroupKey = v),
        Field.Text<SurchargeRule>("baseKey", SurchargeRule.KeyMaxLength, r => r.BaseKey, (r, v) => r.BaseKey = v),
        Field.Text<SurchargeRule>("durationKey", SurchargeRule.KeyMaxLength, r => r.DurationKey, (r, v) => r.DurationKey = v),
        Field.Quantity<SurchargeRule>("freeUnits", r => r.FreeUnits, (r, v) => r.FreeUnits = v ?? 0m),
        Field.JsonList<SurchargeRule>("quantityKeys", SurchargeRule.ListMaxLength, SurchargeRules.IsValidKeyList, CrmMessages.Pricing.SurchargeListInvalid,
            r => r.QuantityKeys, (r, v) => r.QuantityKeys = v),
        Field.JsonList<SurchargeRule>("conditions", SurchargeRule.ListMaxLength, SurchargeRules.IsValidConditionList, CrmMessages.Pricing.SurchargeListInvalid,
            r => r.Conditions, (r, v) => r.Conditions = v),
        Field.Choice<SurchargeRule>("presentation", SurchargeRules.Presentations, r => r.Presentation, (r, v) => r.Presentation = v),
        Field.Flag<SurchargeRule>("stackable", r => r.Stackable, (r, v) => r.Stackable = v),
    ];

    protected override IReadOnlyDictionary<string, RecordFilter<SurchargeRule>> Filters { get; } =
        new Dictionary<string, RecordFilter<SurchargeRule>>
        {
            ["active"] = (q, v, _) => FilterValue.Bool(v) is { } active ? q.Where(r => r.IsActive == active) : null,
            ["kind"] = (q, v, _) => SurchargeRules.Kinds.Contains(v) ? q.Where(r => r.Kind == v) : null,
        };

    protected override IOrderedQueryable<SurchargeRule> Order(IQueryable<SurchargeRule> source) => source.OrderBy(r => r.Priority).ThenBy(r => r.Name);

    // Liste ekranının sıralanabilir başlıkları (ad, tür, öncelik, görünüm, durum, para birimi); sıra her zaman ad ve kimlikle sabitlenir.
    protected override IOrderedQueryable<SurchargeRule> Order(IQueryable<SurchargeRule> source, string? sort) => sort switch
    {
        "name" => source.OrderBy(r => r.Name).ThenBy(r => r.Id),
        "-name" => source.OrderByDescending(r => r.Name).ThenBy(r => r.Id),
        "kind" => source.OrderBy(r => r.Kind).ThenBy(r => r.Name),
        "-kind" => source.OrderByDescending(r => r.Kind).ThenBy(r => r.Name),
        "priority" => source.OrderBy(r => r.Priority).ThenBy(r => r.Name),
        "-priority" => source.OrderByDescending(r => r.Priority).ThenBy(r => r.Name),
        "presentation" => source.OrderBy(r => r.Presentation).ThenBy(r => r.Name),
        "-presentation" => source.OrderByDescending(r => r.Presentation).ThenBy(r => r.Name),
        "isActive" => source.OrderBy(r => r.IsActive).ThenBy(r => r.Name),
        "-isActive" => source.OrderByDescending(r => r.IsActive).ThenBy(r => r.Name),
        _ => Order(source),
    };

    protected override IQueryable<SurchargeRule> Search(IQueryable<SurchargeRule> source, string term) => source.Where(r => r.Name.Contains(term));

    protected override Task AppliedAsync(SurchargeRule entity, JsonObject values, CancellationToken cancellationToken)
    {
        entity.GroupKey = Blank(entity.GroupKey);
        entity.BaseKey = Blank(entity.BaseKey);
        entity.DurationKey = Blank(entity.DurationKey);

        var errors = SurchargeRules.Validate(entity).Select(e => new RecordFieldError(e.Field, e.Message)).ToList();
        if (entity.TaxRatePercent is null)
        {
            // Kuraldan üretilen teklif satırının vergisi bu orandandır; boş bırakılırsa oran uydurulmaz.
            errors.Add(new RecordFieldError("taxRatePercent", CrmMessages.Pricing.SurchargeTaxRequired));
        }

        if (errors.Count > 0)
        {
            return Task.FromException(new RecordValidationException(errors));
        }

        // Türle ilgisiz alanlar boşaltılır: motor onları okumaz, ekranda eski değer kalıntısı olarak görünmesin.
        if (entity.Kind is not (SurchargeRules.Percent or SurchargeRules.Duration))
        {
            entity.BaseKey = null;
        }

        if (entity.Kind != SurchargeRules.Duration)
        {
            entity.DurationKey = null;
            entity.FreeUnits = 0m;
        }

        return Task.CompletedTask;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
