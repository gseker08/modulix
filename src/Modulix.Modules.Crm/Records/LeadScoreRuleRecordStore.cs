using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Leads;

namespace Modulix.Modules.Crm.Records;

/// <summary>
/// Müşteri adayı puan kuralı kayıt deposu (ADR-0050, dilim 1). Okuma ve yazma <c>crm.lead-scoring.manage</c> iznindedir; kurallar puan
/// hesabında aday deposunca iç okuyucuyla okunur (kural bir sunucu kararıdır). Tüm tenant'a uygulanır (veri kapsamı yok).
/// </summary>
internal sealed class LeadScoreRuleRecordStore(CrmStoreContext context) : CrmRecordStore<LeadScoreRule>(context)
{
    public override string EntityKey => CrmModule.Entities.LeadScoreRule;

    protected override string ReadPermission => CrmModule.Permissions.LeadScoringManage;

    protected override string WritePermission => CrmModule.Permissions.LeadScoringManage;

    protected override bool DefaultsOwnerToCurrentUser => false;

    protected override IReadOnlyList<RecordField<LeadScoreRule>> Fields { get; } =
    [
        Field.Text<LeadScoreRule>("name", LeadScoreRule.NameMaxLength, r => r.Name, (r, v) => r.Name = v!, required: true),
        Field.Text<LeadScoreRule>("description", LeadScoreRule.DescriptionMaxLength, r => r.Description, (r, v) => r.Description = v),
        Field.Flag<LeadScoreRule>("isActive", r => r.IsActive, (r, v) => r.IsActive = v),
        Field.Choice<LeadScoreRule>("field", [.. LeadScoring.Fields.Select(f => f.Key)], r => r.Field, (r, v) => r.Field = v),
        Field.Choice<LeadScoreRule>("operator", LeadScoreOperator.All, r => r.Operator, (r, v) => r.Operator = v),
        Field.Text<LeadScoreRule>("value", LeadScoreRule.ValueMaxLength, r => r.Value, (r, v) => r.Value = v),
        Field.Integer<LeadScoreRule>("points", LeadScoreRule.MinPoints, LeadScoreRule.MaxPoints, r => r.Points, (r, v) => r.Points = v),
    ];

    protected override IReadOnlyDictionary<string, RecordFilter<LeadScoreRule>> Filters { get; } =
        new Dictionary<string, RecordFilter<LeadScoreRule>>
        {
            ["active"] = (q, v, _) => FilterValue.Bool(v) is { } active ? q.Where(r => r.IsActive == active) : null,
        };

    protected override IOrderedQueryable<LeadScoreRule> Order(IQueryable<LeadScoreRule> source) => source.OrderBy(r => r.Field).ThenBy(r => r.Name);

    protected override IReadOnlyCollection<string>? SortKeys { get; } = ["name", "-name", "points", "-points"];

    protected override IOrderedQueryable<LeadScoreRule> Order(IQueryable<LeadScoreRule> source, string? sort) => sort switch
    {
        "name" => source.OrderBy(r => r.Name),
        "-name" => source.OrderByDescending(r => r.Name),
        "points" => source.OrderBy(r => r.Points).ThenBy(r => r.Name),
        "-points" => source.OrderByDescending(r => r.Points).ThenBy(r => r.Name),
        _ => Order(source),
    };

    protected override IQueryable<LeadScoreRule> Search(IQueryable<LeadScoreRule> source, string term) => source.Where(r => r.Name.Contains(term));

    protected override Task AppliedAsync(LeadScoreRule entity, JsonObject values, CancellationToken cancellationToken)
    {
        var errors = new List<RecordFieldError>();
        var field = LeadScoring.Find(entity.Field);
        if (field is null)
        {
            errors.Add(new RecordFieldError("field", CrmMessages.Lead.ScoreFieldRequired));
        }
        else if (!LeadScoring.OperatorsFor(field.Kind).Contains(entity.Operator))
        {
            errors.Add(new RecordFieldError("operator", CrmMessages.Lead.ScoreOperatorNotAllowed(field.Label)));
        }

        // Dolu/boş denetiminde değer anlamsızdır; diğer işleçlerde zorunludur.
        if (LeadScoreOperator.IsPresenceCheck(entity.Operator))
        {
            entity.Value = null;
        }
        else if (string.IsNullOrWhiteSpace(entity.Value))
        {
            errors.Add(new RecordFieldError("value", CrmMessages.Lead.ScoreValueRequired));
        }
        else if (entity.Operator == LeadScoreOperator.AtLeast)
        {
            // Sayaç değeri kanonik biçimde saklanır ("03" → "3").
            if (LeadScoring.ParseCount(entity.Value) is { } count)
            {
                entity.Value = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                errors.Add(new RecordFieldError("value", ServerMessages.Validation.IntegerRange(1, LeadScoreOperator.MaxCount)));
            }
        }
        else
        {
            entity.Value = entity.Value.Trim();
        }

        if (entity.Points == 0)
        {
            errors.Add(new RecordFieldError("points", CrmMessages.Lead.ScorePointsNonZero));
        }

        return errors.Count > 0 ? Task.FromException(new RecordValidationException(errors)) : Task.CompletedTask;
    }
}
