using Microsoft.AspNetCore.Http;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Hosting;
using Modulix.Modules.Crm.Contracts.Timeline;
using Modulix.Shared.Crm;

namespace Modulix.Modules.Crm.Endpoints;

/// <summary>
/// GET /api/crm/contacts/{id}/timeline ve /timeline/summary (ADR-0059): kişinin birleşik zaman çizelgesi ve özet şeridi. Firma çizelgesiyle aynı
/// birleştiriciyi kullanır; yalnızca kişiye bağlanabildiğini bildiren kaynaklar (<c>SupportsPerson</c>) çağrılır. Kişi çağıranın kapsamında değilse 404.
/// Firmasız kişi de çalışır; firma bağlamı kişinin ana firmasından gelir.
/// </summary>
internal static class ContactTimelineEndpoint
{
    public static async Task<IResult> GetAsync(
        Guid id, DateTimeOffset? before, int? take, string? sources, IRecordPipeline pipeline, IEnumerable<ICustomerTimelineSource> registered, CancellationToken ct)
    {
        if (TimelineComposer.Validate(take, sources, registered, personScope: true) is { } problem)
        {
            return problem;
        }

        var (query, error) = await QueryAsync(id, before, take ?? CustomerTimelineDto.DefaultTake, pipeline, ct);
        return query is null ? error! : await TimelineComposer.ComposeAsync(query, sources, registered, ct);
    }

    public static async Task<IResult> SummaryAsync(
        Guid id, IRecordPipeline pipeline, IEnumerable<ICustomerTimelineSource> registered, TimeProvider time, CancellationToken ct)
    {
        var (query, error) = await QueryAsync(id, null, 1, pipeline, ct);
        return query is null ? error! : await TimelineComposer.SummarizeAsync(query, registered, time, ct);
    }

    private static async Task<(CustomerTimelineQuery? Query, IResult? Error)> QueryAsync(
        Guid id, DateTimeOffset? before, int take, IRecordPipeline pipeline, CancellationToken ct)
    {
        var contact = await pipeline.GetAsync(new RecordRef(CrmModule.Entities.Contact, id), ct);
        if (!contact.IsSuccess)
        {
            return (null, RecordProblems.ToProblem(contact.Error!));
        }

        var values = contact.Value!.Values;
        var companyId = Guid.TryParse(values["companyId"]?.GetValue<string>(), out var company) ? company : Guid.Empty;
        var personId = Guid.TryParse(values["personId"]?.GetValue<string>(), out var person) ? person : (Guid?)null;
        // Firmanın ortak taraf kaydı, firma kaynaklarının bağlı olduğu kimliktir; kişi sorgusunda kişi kaynakları ortak kişiyle (personId) bağlanır.
        return (new CustomerTimelineQuery(companyId, null, before, take, ContactId: id, PersonId: personId), null);
    }
}
