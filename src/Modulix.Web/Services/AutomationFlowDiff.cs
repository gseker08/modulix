using System.Text.Json;
using System.Text.Json.Nodes;
using Modulix.Shared.Automation;

namespace Modulix.Web.Services;

public enum FlowChangeKind
{
    Added,
    Removed,
    Changed,
    Moved,
}

/// <summary>Bir adımdaki değişiklik; <see cref="StepId"/> kural içindeki adım kimliğidir.</summary>
public sealed record FlowChange(FlowChangeKind Kind, string StepId);

/// <summary>
/// Yayınlanmış sürümle taslak arasındaki fark (ADR-0056, E6-3): tetikleyici, koşul, adımlar (eklenen, kaldırılan, değişen, taşınan) ve sürüm bilgisi (ad, öncelik,
/// diğer kuralları durdurma, geçerlilik). <see cref="MetaChanges"/> anahtarları <c>name</c>, <c>priority</c>, <c>stopProcessing</c>, <c>validity</c>'dir.
/// </summary>
public sealed record FlowDiff(bool TriggerChanged, bool ConditionChanged, IReadOnlyList<FlowChange> Steps, IReadOnlyList<string> MetaChanges)
{
    public bool IsEmpty => !TriggerChanged && !ConditionChanged && Steps.Count == 0 && MetaChanges.Count == 0;
}

/// <summary>
/// İki kural sürümünün tanımını karşılaştırır. Saf ve yan etkisizdir. Adımlar kimlikle eşlenir: kimliği yalnızca taslakta olan eklenmiş, yalnızca yayınlanmışta olan
/// kaldırılmıştır; ikisinde de olanın aksiyonu, parametreleri ya da adım koşulu farklıysa değişmiştir. Ortak adımların birbirine göre sırası değiştiyse (araya yeni
/// adım girmesi tek başına taşıma sayılmaz) yer değiştirenler taşınmış sayılır.
/// </summary>
public static class AutomationFlowDiff
{
    public static FlowDiff Compare(AutomationVersionDto published, AutomationVersionDto draft)
    {
        var (before, after) = (Parse(published.Definition), Parse(draft.Definition));
        var steps = Steps(before, after);

        var meta = new List<string>();
        if (published.Name != draft.Name)
        {
            meta.Add("name");
        }

        if (published.Priority != draft.Priority)
        {
            meta.Add("priority");
        }

        if (published.StopProcessing != draft.StopProcessing)
        {
            meta.Add("stopProcessing");
        }

        if (published.ActiveFrom != draft.ActiveFrom || published.ActiveUntil != draft.ActiveUntil)
        {
            meta.Add("validity");
        }

        return new FlowDiff(!Same(before?["trigger"], after?["trigger"]), !Same(before?["condition"], after?["condition"]), steps, meta);
    }

    private static List<FlowChange> Steps(JsonObject? before, JsonObject? after)
    {
        var old = Steps(before);
        var current = Steps(after);
        var changes = new List<FlowChange>();

        changes.AddRange(current.Where(s => !old.Any(o => o.Id == s.Id)).Select(s => new FlowChange(FlowChangeKind.Added, s.Id)));
        changes.AddRange(old.Where(o => !current.Any(s => s.Id == o.Id)).Select(o => new FlowChange(FlowChangeKind.Removed, o.Id)));

        var common = current.Where(s => old.Any(o => o.Id == s.Id)).ToList();
        foreach (var step in common)
        {
            if (!Same(old.First(o => o.Id == step.Id).Node, step.Node))
            {
                changes.Add(new FlowChange(FlowChangeKind.Changed, step.Id));
            }
        }

        // Ortak adımların her iki sürümdeki sırası: aynı konumda olmayan adım taşınmıştır (eklenen/kaldırılan adımlar sırayı bozmaz).
        var oldOrder = old.Where(o => common.Any(s => s.Id == o.Id)).Select(o => o.Id).ToList();
        var newOrder = common.Select(s => s.Id).ToList();
        for (var i = 0; i < newOrder.Count; i++)
        {
            if (newOrder[i] != oldOrder[i])
            {
                changes.Add(new FlowChange(FlowChangeKind.Moved, newOrder[i]));
            }
        }

        return changes;
    }

    private static List<(string Id, JsonNode Node)> Steps(JsonObject? definition) =>
        [.. (definition?["actions"] as JsonArray ?? []).OfType<JsonObject>().Select(a => (Id: a["id"]?.GetValue<string>() ?? string.Empty, Node: (JsonNode)a))];

    private static JsonObject? Parse(JsonElement definition) =>
        definition.ValueKind == JsonValueKind.Object ? JsonNode.Parse(definition.GetRawText()) as JsonObject : null;

    private static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);
}
