using Microsoft.AspNetCore.Http;
using Modulix.Modules.Crm.Contracts.Timeline;
using Modulix.Shared.Crm;

namespace Modulix.Modules.Crm.Endpoints;

/// <summary>
/// Firma ve kişi çizelgesinin ortak birleştiricisi (ADR-0053, ADR-0059). Kaynaklar kayıtlıdır (<see cref="ICustomerTimelineSource"/>): hangi kaynakların
/// çizelgeye girdiği koda yazılmaz, kayıtlı kaynaklardan ve her kaynağın kendi bildiriminden (kişi için <c>SupportsPerson</c>) gelir. Her kaynak
/// kendi izin, veri kapsamı ve lisans kuralını uygular; birleştirici hiçbir yetki vermez. Sayfalama imleçlidir: <c>before</c> verilirse o andan önceki satırlar gelir.
/// Süzgeç: <c>sources</c> virgülle ayrılmış kaynak anahtarlarıdır; tanınmayan anahtar doğrulama hatası (400) döner; izni ya da lisansı olmayan kaynağın anahtarı geçerlidir ama satır getirmez.
/// </summary>
internal static class TimelineComposer
{
    /// <summary>Kaynak adı kayıtlı değilse ya da sayı sınırı aşılmışsa doğrulama yanıtı; yoksa <c>null</c>.</summary>
    public static IResult? Validate(int? take, string? sources, IEnumerable<ICustomerTimelineSource> registered, bool personScope)
    {
        if (take is < 1 or > CustomerTimelineDto.MaxTake)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["take"] = [$"Satır sayısı 1 ile {CustomerTimelineDto.MaxTake} arasında olmalıdır."],
            });
        }

        var known = Eligible(registered, personScope).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        return ParseSources(sources).FirstOrDefault(key => !known.Contains(key)) is { } unknown
            ? Results.ValidationProblem(new Dictionary<string, string[]> { ["sources"] = [$"Tanınmayan kaynak: {unknown}."] })
            : null;
    }

    /// <summary>Sorgunun türüne uyan kaynaklar: kişi sorgusunda yalnızca kişiyi destekleyenler (en çok bir kez, anahtar sırasıyla).</summary>
    public static IEnumerable<ICustomerTimelineSource> Eligible(IEnumerable<ICustomerTimelineSource> registered, bool personScope) =>
        registered.Where(r => !personScope || r.SupportsPerson).OrderBy(r => r.Key, StringComparer.Ordinal);

    public static async Task<IResult> ComposeAsync(
        CustomerTimelineQuery query, string? sources, IEnumerable<ICustomerTimelineSource> registered, CancellationToken ct)
    {
        var wanted = ParseSources(sources);
        // Her kaynak imleçten önceki en yeni "Take" satırını verir; birleşimin en yeni "Take" satırı bu yüzden eksiksizdir.
        var readable = new List<string>();
        var entries = new List<CustomerTimelineEntry>();
        var more = false;
        foreach (var source in Eligible(registered, query.IsPerson).Where(r => wanted.Count == 0 || wanted.Contains(r.Key)))
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
        var page = ordered.Take(query.Take).ToList();
        more |= ordered.Count > page.Count;
        return Results.Ok(new CustomerTimelineDto(
            [.. page.Select(e => new CustomerTimelineItemDto(e.EntityKey, e.RecordId, e.OccurredAt, e.Title, e.Status, e.Type, e.OwnerId, e.Reference))],
            more && page.Count > 0 ? page[^1].OccurredAt : null,
            readable));
    }

    /// <summary>Özet şeridi: son hareket ("şimdi"den önceki en yeni satır) ve kaynakların açık iş sayıları.</summary>
    public static async Task<IResult> SummarizeAsync(
        CustomerTimelineQuery query, IEnumerable<ICustomerTimelineSource> registered, TimeProvider time, CancellationToken ct)
    {
        // Son hareket: imleç sınırı hariçtir; ileri tarihli planlı aktiviteler son hareket sayılmaz.
        var lastQuery = query with { Before = time.GetUtcNow(), Take = 1 };
        var countQuery = query with { Before = null, Take = 1 };
        CustomerTimelineEntry? last = null;
        var open = new List<CustomerTimelineOpenDto>();
        foreach (var source in Eligible(registered, query.IsPerson))
        {
            if (await source.ReadAsync(lastQuery, ct) is not { } slice)
            {
                continue;
            }

            if (slice.Entries.FirstOrDefault() is { } entry && (last is null || entry.OccurredAt > last.OccurredAt))
            {
                last = entry;
            }

            if (await source.CountOpenAsync(countQuery, ct) is { } count)
            {
                open.Add(new CustomerTimelineOpenDto(source.Key, count));
            }
        }

        return Results.Ok(new CustomerTimelineSummaryDto(last?.OccurredAt, last?.EntityKey, open));
    }

    private static HashSet<string> ParseSources(string? value) =>
        [.. (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
