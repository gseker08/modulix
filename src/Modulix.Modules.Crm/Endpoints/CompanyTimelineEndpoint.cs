using Microsoft.AspNetCore.Http;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Hosting;
using Modulix.Modules.Crm.Contracts.Timeline;
using Modulix.Shared.Crm;

namespace Modulix.Modules.Crm.Endpoints;

/// <summary>
/// GET /api/crm/companies/{id}/timeline (CRM eksik süreçler E9): firmanın birleşik zaman çizelgesi. Satırlar kayıtlı kaynaklardan
/// (<see cref="ICustomerTimelineSource"/>) gelir; her kaynak kendi izin, veri kapsamı ve lisans kuralını uygular, bu uç yalnızca birleştirir.
/// Firma çağıranın kapsamında değilse 404 döner. Sayfalama imleçlidir: <c>before</c> verilirse o andan önceki satırlar gelir.
/// Süzgeç (dilim 3): <c>sources</c> virgülle ayrılmış kaynak anahtarlarıdır; verilirse yalnızca onlar okunur (yanıttaki <c>sources</c> listesi de
/// yalnızca sorgulananlardan okunabilenleri sayar). Tanınmayan anahtar 400 döner; izni ya da lisansı olmayan kaynağın anahtarı geçerlidir ama satır getirmez.
/// </summary>
internal static class CompanyTimelineEndpoint
{
    public static async Task<IResult> GetAsync(
        Guid id, DateTimeOffset? before, int? take, string? sources, IRecordPipeline pipeline, IEnumerable<ICustomerTimelineSource> registered, CancellationToken ct)
    {
        if (TimelineComposer.Validate(take, sources, registered, personScope: false) is { } problem)
        {
            return problem;
        }

        var company = await pipeline.GetAsync(new RecordRef(CrmModule.Entities.Company, id), ct);
        if (!company.IsSuccess)
        {
            return RecordProblems.ToProblem(company.Error!);
        }

        var partyId = Guid.TryParse(company.Value!.Values["partyId"]?.GetValue<string>(), out var party) ? party : (Guid?)null;
        var size = take ?? CustomerTimelineDto.DefaultTake;
        return await TimelineComposer.ComposeAsync(new CustomerTimelineQuery(id, partyId, before, size), sources, registered, ct);
    }
}
