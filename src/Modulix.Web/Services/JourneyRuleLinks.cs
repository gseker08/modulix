using System.Text.Json;
using Modulix.Shared.Automation;

namespace Modulix.Web.Services;

/// <summary>Bir kuralın yolculuğa hangi sürümüyle bağlı olduğu.</summary>
public enum JourneyRuleBinding
{
    None,
    Published,
    DraftOnly,
}

/// <summary>
/// Yolculuğa bağlı kuralı bulma (ADR-0056): bağ, kural koşulundaki <c>marketing.journey-enrollment.journeyId</c> satırıyla kurulur (yolculuk kaydında kural
/// alanı yoktur). Bu sınıf saf ve yan etkisizdir; koşulun kendisini (iç içe gruplar dahil) tarar, böylece düzenleyicinin gösteremediği koşullar da bulunur.
/// </summary>
public static class JourneyRuleLinks
{
    public const string EntityKey = "marketing.journey-enrollment";

    public const string JourneyField = "marketing.journey-enrollment.journeyId";

    /// <summary>Kural türü yolculuk kaydı mı? Yalnızca öyle kurallar yolculuğa bağlanabilir.</summary>
    public static bool IsJourneyRule(AutomationRuleSummaryDto rule) => rule.EntityKey == EntityKey;

    /// <summary>Tanımın koşulu bu yolculuğun kimliğini <c>journeyId</c> alanında anıyor mu?</summary>
    public static bool References(JsonElement definition, Guid journeyId)
    {
        if (definition.ValueKind != JsonValueKind.Object || !definition.TryGetProperty("condition", out var condition) || condition.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return Walk(condition, journeyId.ToString());
    }

    /// <summary>Yayınlanmış sürüm bağlıysa kural çalışır; yalnızca taslak bağlıysa yayınlanınca bağlanır.</summary>
    public static JourneyRuleBinding BindingOf(AutomationRuleDetailDto detail, Guid journeyId)
    {
        if (detail.Published is { } published && References(published.Definition, journeyId))
        {
            return JourneyRuleBinding.Published;
        }

        return detail.Draft is { } draft && References(draft.Definition, journeyId) ? JourneyRuleBinding.DraftOnly : JourneyRuleBinding.None;
    }

    private static bool Walk(JsonElement node, string journeyId)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "leaf")
        {
            return node.TryGetProperty("field", out var field) && field.ValueKind == JsonValueKind.String && field.GetString() == JourneyField
                && node.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array
                && values.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), journeyId, StringComparison.OrdinalIgnoreCase));
        }

        return node.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.EnumerateArray().Any(i => Walk(i, journeyId));
    }
}
