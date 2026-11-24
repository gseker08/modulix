using System.Text.Json;
using Modulix.Shared.Automation;

namespace Modulix.Web.Services;

/// <summary>Akış çiziminde bir düğümün türü (ADR-0056, E6-1): biçimi belirler, davranışı değil.</summary>
public enum FlowNodeKind
{
    Trigger,
    Condition,
    Action,
    Wait,
    Stop,
}

/// <summary>Akıştaki bir düğüm: tür etiketi, başlık ve ayrıntı satırları. <see cref="Branch"/> adımın koşula bağlı olduğunu gösterir.</summary>
public sealed record FlowNode(FlowNodeKind Kind, string KindLabel, string Title, IReadOnlyList<string> Lines, IReadOnlyList<string> BranchLines)
{
    /// <summary>Adım koşulu varsa (dallanma, ADR-0027 4c-5) koşul satırları; yoksa boş.</summary>
    public bool Branch => BranchLines.Count > 0;
}

/// <summary>
/// Otomasyon kural sürümünü (ya da yolculuğun kural sürümünü) salt okunur akışa çevirir (ADR-0056, E6-1). Yeni bir model değildir: aynı tanım
/// JSON'u <see cref="EditorDefinition"/> ile okunur ve sırayla tetikleyici → koşul → adımlar olarak dizilir. Saf ve yan etkisizdir; görüntüsü
/// <c>FlowView</c> bileşenindedir. Katalogda bulunamayan alan ya da aksiyon silinmez, teknik anahtarıyla gösterilir.
/// </summary>
public static class AutomationFlow
{
    private const string WaitAction = "core.flow.wait";
    private const string StopAction = "core.flow.stop";

    public static IReadOnlyList<FlowNode> Build(JsonElement definition, AutomationEntityDto? entity, IReadOnlyList<AutomationActionDto> actions)
    {
        var parsed = EditorDefinition.Parse(definition);
        var nodes = new List<FlowNode>
        {
            new(FlowNodeKind.Trigger, Texts.Get("admin.flow.trigger"), AutomationLabels.TriggerType(parsed.Trigger.Type), TriggerLines(parsed.Trigger, entity), []),
        };

        var condition = ConditionLines(parsed.Condition, entity);
        if (condition.Count > 0)
        {
            nodes.Add(new FlowNode(FlowNodeKind.Condition, Texts.Get("admin.flow.condition"),
                parsed.Condition.IsLocked ? Texts.Get("admin.flow.conditionAdvanced") : AutomationLabels.Logic(parsed.Condition.Logic),
                parsed.Condition.IsLocked ? [] : condition, []));
        }

        for (var i = 0; i < parsed.Actions.Count; i++)
        {
            var step = parsed.Actions[i];
            var definitionOfAction = actions.FirstOrDefault(a => a.Key == step.Action);
            var kind = step.Action switch
            {
                WaitAction => FlowNodeKind.Wait,
                StopAction => FlowNodeKind.Stop,
                _ => FlowNodeKind.Action,
            };

            nodes.Add(new FlowNode(
                kind, Texts.Get("admin.flow.step", i + 1), definitionOfAction?.DisplayName ?? step.Action,
                ParameterLines(step, definitionOfAction, entity), ConditionLines(step.When, entity)));
        }

        return nodes;
    }

    private static IReadOnlyList<string> TriggerLines(EditorTrigger trigger, AutomationEntityDto? entity)
    {
        string Field(string key) => FieldLabel(entity, key);
        return trigger.Type switch
        {
            EditorTrigger.FieldChanged when trigger.Fields.Count > 0 => [Texts.Get("admin.flow.triggerFields", string.Join(", ", trigger.Fields.Select(Field)))],
            EditorTrigger.StatusTransition => [string.IsNullOrWhiteSpace(trigger.From)
                ? Texts.Get("admin.flow.triggerTransitionAny", Field(trigger.Field), trigger.To)
                : Texts.Get("admin.flow.triggerTransition", Field(trigger.Field), trigger.From, trigger.To)],
            EditorTrigger.Scheduled => [Texts.Get("admin.flow.triggerTime", trigger.TimeOfDay)],
            EditorTrigger.DateRelative => [trigger.OffsetDays switch
            {
                < 0 => Texts.Get("admin.flow.triggerDateBefore", Field(trigger.Field), -trigger.OffsetDays),
                > 0 => Texts.Get("admin.flow.triggerDateAfter", Field(trigger.Field), trigger.OffsetDays),
                _ => Texts.Get("admin.flow.triggerDateSame", Field(trigger.Field)),
            }],
            EditorTrigger.Inactivity => [Texts.Get("admin.flow.triggerInactivity", trigger.Days)],
            EditorTrigger.DomainEvent => [entity?.Events.FirstOrDefault(e => e.Key == trigger.EventKey)?.DisplayName ?? trigger.EventKey],
            _ when trigger.IsLocked => [Texts.Get("admin.flow.triggerRaw")],
            _ => [],
        };
    }

    private static IReadOnlyList<string> ConditionLines(EditorCondition condition, AutomationEntityDto? entity)
    {
        if (condition.IsLocked)
        {
            return [Texts.Get("admin.flow.conditionRaw")];
        }

        return [.. condition.Leaves.Select(leaf =>
        {
            var op = AutomationLabels.Operator(leaf.Operator);
            var field = FieldLabel(entity, leaf.Field);
            return leaf.Values.Count == 0 ? $"{field} {op}" : $"{field} {op}: {string.Join(", ", leaf.Values)}";
        })];
    }

    private static IReadOnlyList<string> ParameterLines(EditorAction step, AutomationActionDto? action, AutomationEntityDto? entity)
    {
        var lines = new List<string>();
        foreach (var (name, value) in step.Parameters)
        {
            var label = action?.Parameters.FirstOrDefault(p => p.Name == name)?.Label ?? name;
            var bound = step.BoundField(name);
            var text = bound is not null ? Texts.Get("admin.flow.paramBound", FieldLabel(entity, bound)) : value?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add($"{label}: {text}");
            }
        }

        return lines;
    }

    private static string FieldLabel(AutomationEntityDto? entity, string key) =>
        entity?.Fields.FirstOrDefault(f => f.Key == key)?.Label
        ?? entity?.Relations.FirstOrDefault(r => r.Key == key)?.Label
        ?? key;
}
