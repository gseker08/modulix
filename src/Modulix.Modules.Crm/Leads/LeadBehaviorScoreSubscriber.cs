using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Events;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Crm.Records;

namespace Modulix.Modules.Crm.Leads;

/// <summary>
/// Davranış puanı (ADR-0050, dilim 2): adaya bağlı aktivite eklenince, değişince ya da silinince adayın puanı hemen yeniden hesaplanır
/// (<c>crm.lead-behavior-score</c>). Etkilenen adaylar olay farkındaki <c>leadId</c> eski/yeni değerleri ve aktivitenin bugünkü adayıdır (aktivite
/// başka adaya taşınınca ikisi de). Puan yalnızca değişecekse, kayıt boru hattından boş değerli güncellemeyle yazılır; tekrar teslimde puan zaten
/// doğru olduğu için ikinci yazım olmaz. Kaçan durum (ör. aynı kişiden yeni başvuru) toplu yeniden puanlama işiyle (<see cref="LeadScoreJob"/>) düzelir.
/// </summary>
internal sealed class LeadBehaviorScoreSubscriber(LeadScoreReader reader, IRecordPipeline pipeline) : IEventSubscriber
{
    public const string Key = "crm.lead-behavior-score";

    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        HashSet<Guid> leads = [];
        if (envelope.Payload["leadId"] is JsonObject change)
        {
            AddLead(leads, change["old"]);
            AddLead(leads, change["new"]);
        }

        if (await reader.LeadOfActivityAsync(envelope.RecordId, cancellationToken) is { } current)
        {
            leads.Add(current);
        }

        foreach (Guid leadId in leads)
        {
            // Sonuç bilerek yok sayılır: aday doğrulamadan dönerse olay kalıcı hata vermez; toplu iş sonraki turda yeniden dener.
            if (await reader.IsStaleAsync(leadId, cancellationToken))
            {
                _ = await pipeline.UpdateAsync(new RecordCommand(CrmModule.Entities.Lead, leadId, new JsonObject()), cancellationToken);
            }
        }
    }

    private static void AddLead(HashSet<Guid> leads, JsonNode? value)
    {
        if (value is JsonValue node && node.TryGetValue(out string? text) && Guid.TryParse(text, out Guid id))
        {
            leads.Add(id);
        }
    }
}
