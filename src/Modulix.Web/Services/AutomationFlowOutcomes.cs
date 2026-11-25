using Modulix.Shared.Automation;

namespace Modulix.Web.Services;

/// <summary>Deneme çalıştırmasında (önizleme) bir adımın örnek kayıtta nasıl sonuçlanacağı; hiçbir şey çalıştırılmaz.</summary>
public enum FlowOutcome
{
    /// <summary>Kural koşulu ve adım koşulu sağlanıyor, izin var: gerçek çalışmada çalışırdı.</summary>
    WouldRun,

    /// <summary>Adım koşulu örnek kayıtta sağlanmıyor: gerçek çalışmada adım atlanır, akış sürer.</summary>
    SkippedByStepCondition,

    /// <summary>Aksiyon bu kimlikle ya da şu an kullanılamıyor (izin ya da kapalı modül).</summary>
    NotPermitted,

    /// <summary>Kural koşulu örnek kayıtta sağlanmıyor: kural hiç çalışmaz.</summary>
    RuleNotMatched,
}

/// <summary>
/// Deneme çalıştırması sonucunu (ADR-0056, E6-3b) akıştaki adımlara eşler. Saf ve yan etkisizdir. Önizleme örnek kayıtla değerlendirilmediyse (örnek kayıt seçilmedi
/// ya da tanım geçersiz) eşleme boştur: akışa sonuç işareti konmaz.
/// </summary>
public static class AutomationFlowOutcomes
{
    public static IReadOnlyDictionary<string, FlowOutcome> From(AutomationPreviewDto? preview, IEnumerable<string> stepIds)
    {
        var result = new Dictionary<string, FlowOutcome>();
        if (preview is not { Evaluated: true })
        {
            return result;
        }

        if (!preview.ConditionsPassed)
        {
            foreach (var id in stepIds.Where(id => !string.IsNullOrEmpty(id)))
            {
                result[id] = FlowOutcome.RuleNotMatched;
            }

            return result;
        }

        foreach (var planned in preview.PlannedActions)
        {
            result[planned.StepId] = !planned.Permitted
                ? FlowOutcome.NotPermitted
                : planned.StepConditionPassed == false ? FlowOutcome.SkippedByStepCondition : FlowOutcome.WouldRun;
        }

        return result;
    }
}
