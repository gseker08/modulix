using Microsoft.AspNetCore.Http;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Hosting;
using Modulix.Modules.Crm.Contracts.Timeline;
using Modulix.Shared.Crm;

namespace Modulix.Modules.Crm.Endpoints;

/// <summary>
/// GET /api/crm/companies/{id}/timeline/summary (ADR-0053, dilim 3): zaman çizelgesinin özet şeridi. Her kaynak çağıranın kimliğiyle ve kendi izin,
/// veri kapsamı ve lisans kuralıyla sorulur; okunamayan kaynak ne son hareketi ne açık iş sayısını etkiler. Firma çağıranın kapsamında değilse 404.
/// </summary>
internal static class CompanyTimelineSummaryEndpoint
{
    public static async Task<IResult> GetAsync(
        Guid id, IRecordPipeline pipeline, IEnumerable<ICustomerTimelineSource> registered, TimeProvider time, CancellationToken ct)
    {
        var company = await pipeline.GetAsync(new RecordRef(CrmModule.Entities.Company, id), ct);
        if (!company.IsSuccess)
        {
            return RecordProblems.ToProblem(company.Error!);
        }

        var partyId = Guid.TryParse(company.Value!.Values["partyId"]?.GetValue<string>(), out var party) ? party : (Guid?)null;

        return await TimelineComposer.SummarizeAsync(new CustomerTimelineQuery(id, partyId, null, 1), registered, time, ct);
    }
}
