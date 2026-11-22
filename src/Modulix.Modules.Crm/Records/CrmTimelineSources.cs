using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Persistence;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Crm.Contracts.Timeline;
using Modulix.Modules.Crm.Domain;

namespace Modulix.Modules.Crm.Records;

/// <summary>
/// Müşteri zaman çizelgesinin CRM kaynakları (CRM eksik süreçler E9, dilim 1): aktivite, fırsat ve teklif. Satırlar kayıt boru hattından okunur; izin,
/// veri kapsamı, lisans ve özel aktivite maskesi orada uygulanır. Boru hattı oluşturulma anını dışarı vermediği için o an, boru hattının zaten
/// görünür bulduğu kayıt kimlikleriyle modülün kendi tablosundan okunur (yeni kayıt alanı açılmaz).
/// </summary>
internal abstract class CrmTimelineSource(IRecordPipeline pipeline) : ICustomerTimelineSource
{
    public abstract string Key { get; }

    protected abstract string EntityKey { get; }

    /// <summary>Firmaya göre süzgeç, en yeniden eskiye sıralama ve (verildiyse) imleç.</summary>
    protected abstract Dictionary<string, string> Filters(CustomerTimelineQuery query, string? before);

    protected abstract Task<IReadOnlyList<CustomerTimelineEntry>> ToEntriesAsync(IReadOnlyList<RecordData> items, CancellationToken cancellationToken);

    public async Task<CustomerTimelineSlice?> ReadAsync(CustomerTimelineQuery query, CancellationToken cancellationToken)
    {
        var before = query.Before?.ToString("O", CultureInfo.InvariantCulture);
        var take = Math.Clamp(query.Take, 1, RecordQuery.MaxPageSize);
        var page = await pipeline.QueryAsync(new RecordQuery(EntityKey, 1, take, Filters: Filters(query, before)), cancellationToken);
        if (!page.IsSuccess)
        {
            // Çağıran bu türü okuyamıyorsa kaynak yoktur; başka hata sessizce boş çizelgeye çevrilmez.
            return page.Error!.Code is RecordErrorCode.PermissionDenied or RecordErrorCode.FeatureNotLicensed
                ? null
                : throw new InvalidOperationException($"Zaman çizelgesi okunamadı ({Key}): {page.Error.Message}");
        }

        var entries = await ToEntriesAsync(page.Value!.Items, cancellationToken);
        return new CustomerTimelineSlice(entries, page.Value.TotalCount > page.Value.Items.Count);
    }

    /// <summary>Açık iş süzgeci (<c>open=true</c>); türün açık işi yoksa <c>null</c>.</summary>
    protected virtual bool CountsOpen => false;

    public async Task<int?> CountOpenAsync(CustomerTimelineQuery query, CancellationToken cancellationToken)
    {
        if (!CountsOpen)
        {
            return null;
        }

        var filters = Filters(query, before: null);
        filters.Remove("sort");
        filters["open"] = "true";
        var page = await pipeline.QueryAsync(new RecordQuery(EntityKey, 1, 1, Filters: filters), cancellationToken);
        return page.IsSuccess
            ? page.Value!.TotalCount
            : page.Error!.Code is RecordErrorCode.PermissionDenied or RecordErrorCode.FeatureNotLicensed
                ? null
                : throw new InvalidOperationException($"Açık iş sayılamadı ({Key}): {page.Error.Message}");
    }

    protected static string? Text(JsonObject values, string name) => values[name]?.GetValue<string>();

    protected static Guid? Id(JsonObject values, string name) => Guid.TryParse(Text(values, name), out var id) ? id : null;

    protected static DateTimeOffset? Time(JsonObject values, string name) =>
        DateTimeOffset.TryParse(Text(values, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : null;
}

internal sealed class ActivityTimelineSource(IRecordPipeline pipeline, IModuleData data) : CrmTimelineSource(pipeline)
{
    public override string Key => "crm.activities";

    protected override bool CountsOpen => true;

    protected override string EntityKey => CrmModule.Entities.Activity;

    protected override Dictionary<string, string> Filters(CustomerTimelineQuery query, string? before)
    {
        // timeline: firmanın kişilerine ve fırsatlarına bağlı aktiviteler de gelir (firma detayındaki aktivite listesiyle aynı kapsam).
        var filters = new Dictionary<string, string>
        {
            ["timeline"] = $"{CrmModule.Entities.Company}:{query.CompanyId}",
            ["sort"] = ActivityRecordStore.OccurredDescending,
        };
        if (before is not null)
        {
            filters["occurredBefore"] = before;
        }

        return filters;
    }

    protected override async Task<IReadOnlyList<CustomerTimelineEntry>> ToEntriesAsync(IReadOnlyList<RecordData> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.Id).ToList();
        var created = await data.Set<Activity>().AsNoTracking().Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.CreatedAt, cancellationToken);
        return
        [
            .. items.Select(i => new CustomerTimelineEntry(
                EntityKey,
                i.Id,
                Time(i.Values, "completedAt") ?? Time(i.Values, "startAt") ?? Time(i.Values, "dueAt") ?? created[i.Id],
                Text(i.Values, "subject") ?? string.Empty,
                Text(i.Values, "status"),
                Text(i.Values, "type"),
                Id(i.Values, OwnedRecordType.OwnerField))),
        ];
    }
}

internal sealed class OpportunityTimelineSource(IRecordPipeline pipeline, IModuleData data) : CrmTimelineSource(pipeline)
{
    public override string Key => "crm.opportunities";

    protected override bool CountsOpen => true;

    protected override string EntityKey => CrmModule.Entities.Opportunity;

    protected override Dictionary<string, string> Filters(CustomerTimelineQuery query, string? before)
    {
        var filters = new Dictionary<string, string> { ["companyId"] = query.CompanyId.ToString(), ["sort"] = "-created" };
        if (before is not null)
        {
            filters["createdBefore"] = before;
        }

        return filters;
    }

    protected override async Task<IReadOnlyList<CustomerTimelineEntry>> ToEntriesAsync(IReadOnlyList<RecordData> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.Id).ToList();
        var created = await data.Set<Opportunity>().AsNoTracking().Where(o => ids.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.CreatedAt, cancellationToken);
        return
        [
            .. items.Select(i => new CustomerTimelineEntry(
                EntityKey,
                i.Id,
                created[i.Id],
                Text(i.Values, "title") ?? string.Empty,
                Text(i.Values, "status"),
                OwnerId: Id(i.Values, OwnedRecordType.OwnerField),
                Reference: Text(i.Values, "opportunityNumber"))),
        ];
    }
}

internal sealed class QuoteTimelineSource(IRecordPipeline pipeline, IModuleData data) : CrmTimelineSource(pipeline)
{
    public override string Key => "crm.quotes";

    protected override string EntityKey => CrmModule.Entities.Quote;

    protected override Dictionary<string, string> Filters(CustomerTimelineQuery query, string? before)
    {
        // Her revizyon ayrı satırdır: revizyonun çıkarıldığı an müşteri geçmişinin parçasıdır.
        var filters = new Dictionary<string, string> { ["companyId"] = query.CompanyId.ToString(), ["sort"] = "-createdAt" };
        if (before is not null)
        {
            filters["createdBefore"] = before;
        }

        return filters;
    }

    protected override async Task<IReadOnlyList<CustomerTimelineEntry>> ToEntriesAsync(IReadOnlyList<RecordData> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.Id).ToList();
        var created = await data.Set<Quote>().AsNoTracking().Where(q => ids.Contains(q.Id))
            .ToDictionaryAsync(q => q.Id, q => q.CreatedAt, cancellationToken);
        return
        [
            .. items.Select(i => new CustomerTimelineEntry(
                EntityKey,
                i.Id,
                created[i.Id],
                Text(i.Values, "title") ?? string.Empty,
                Text(i.Values, "status"),
                OwnerId: Id(i.Values, OwnedRecordType.OwnerField),
                Reference: Text(i.Values, "quoteNumber"))),
        ];
    }
}
