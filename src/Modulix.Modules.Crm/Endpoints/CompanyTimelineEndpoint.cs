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
/// yalnızca sorgulananlardan okunabilenleri sayar). Tanınmayan anahtar 422 döner; izni ya da lisansı olmayan kaynağın anahtarı geçerlidir ama satır getirmez.
/// </summary>
internal static class CompanyTimelineEndpoint
{
    public static async Task<IResult> GetAsync(
        Guid id, DateTimeOffset? before, int? take, string? sources, IRecordPipeline pipeline, IEnumerable<ICustomerTimelineSource> registered, CancellationToken ct)
    {
        if (take is < 1 or > CustomerTimelineDto.MaxTake)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["take"] = [$"Satır sayısı 1 ile {CustomerTimelineDto.MaxTake} arasında olmalıdır."],
            });
        }

        var wanted = ParseSources(sources);
        var known = registered.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        if (wanted.FirstOrDefault(key => !known.Contains(key)) is { } unknown)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sources"] = [$"Tanınmayan kaynak: {unknown}."],
            });
        }

        var company = await pipeline.GetAsync(new RecordRef(CrmModule.Entities.Company, id), ct);
        if (!company.IsSuccess)
        {
            return RecordProblems.ToProblem(company.Error!);
        }

        var size = take ?? CustomerTimelineDto.DefaultTake;
        var partyId = Guid.TryParse(company.Value!.Values["partyId"]?.GetValue<string>(), out var party) ? party : (Guid?)null;
        var query = new CustomerTimelineQuery(id, partyId, before, size);

        // Her kaynak imleçten önceki en yeni "size" satırını verir; birleşimin en yeni "size" satırı bu yüzden eksiksizdir.
        var readable = new List<string>();
        var entries = new List<CustomerTimelineEntry>();
        var more = false;
        foreach (var source in registered.Where(r => wanted.Count == 0 || wanted.Contains(r.Key)).OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (await source.ReadAsync(query, ct) is not { } slice)
            {
                continue;
            }

            readable.Add(source.Key);
            entries.AddRange(slice.Entries);
            more |= slice.HasMore;
        }

        var ordered = entries.OrderByDescending(e => e.OccurredAt).ThenBy(e => e.RecordId).ToList();
        var page = ordered.Take(size).ToList();
        more |= ordered.Count > page.Count;
        return Results.Ok(new CustomerTimelineDto(
            [.. page.Select(e => new CustomerTimelineItemDto(e.EntityKey, e.RecordId, e.OccurredAt, e.Title, e.Status, e.Type, e.OwnerId, e.Reference))],
            more && page.Count > 0 ? page[^1].OccurredAt : null,
            readable));
    }

    private static HashSet<string> ParseSources(string? value) =>
        [.. (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
