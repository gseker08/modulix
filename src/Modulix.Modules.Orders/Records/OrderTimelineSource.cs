using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Persistence;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Crm.Contracts.Timeline;
using Modulix.Modules.Orders.Domain;
using Modulix.Modules.Orders.Lifecycle;

namespace Modulix.Modules.Orders.Records;

/// <summary>
/// Müşteri zaman çizelgesinin sipariş kaynağı (ADR-0053, dilim 2): müşterinin siparişleri, oluşturulma anıyla. Siparişler ortak taraf kaydına
/// (<c>partyId</c>) bağlıdır; satırlar kayıt boru hattından okunur, izin, veri kapsamı ve lisans orada uygulanır. Boru hattı oluşturulma anını
/// dışarı vermediği için o an, boru hattının görünür bulduğu kimliklerle modülün kendi tablosundan okunur.
/// </summary>
internal sealed class OrderTimelineSource(IRecordPipeline pipeline, IModuleData data) : ICustomerTimelineSource
{
    public bool SupportsPerson => true;

    public string Key => "orders.orders";

    public async Task<CustomerTimelineSlice?> ReadAsync(CustomerTimelineQuery query, CancellationToken cancellationToken)
    {
        if (query.SharedRecordScope is not { } scope)
        {
            // Ortak taraf kaydı olmayan firmanın siparişi olamaz: okunabilir, boş kaynak.
            return new CustomerTimelineSlice([], false);
        }

        var filters = new Dictionary<string, string> { [scope.Field] = scope.Id.ToString() };
        if (query.Before is { } before)
        {
            filters["createdBefore"] = before.ToString("O", CultureInfo.InvariantCulture);
        }

        var take = Math.Clamp(query.Take, 1, RecordQuery.MaxPageSize);
        var page = await pipeline.QueryAsync(new RecordQuery(OrdersModule.Entities.Order, 1, take, Filters: filters), cancellationToken);
        if (!page.IsSuccess)
        {
            // Çağıran siparişleri okuyamıyorsa kaynak yoktur; başka hata sessizce boş çizelgeye çevrilmez.
            return page.Error!.Code is RecordErrorCode.PermissionDenied or RecordErrorCode.FeatureNotLicensed
                ? null
                : throw new InvalidOperationException($"Zaman çizelgesi okunamadı ({Key}): {page.Error.Message}");
        }

        var items = page.Value!.Items;
        var ids = items.Select(i => i.Id).ToList();
        var created = await data.Set<SalesOrder>().AsNoTracking().Where(o => ids.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.CreatedAt, cancellationToken);
        return new CustomerTimelineSlice(
            [
                .. items.Select(i => new CustomerTimelineEntry(
                    OrdersModule.Entities.Order,
                    i.Id,
                    created[i.Id],
                    Text(i.Values, "title") ?? string.Empty,
                    Text(i.Values, "status"),
                    OwnerId: Guid.TryParse(Text(i.Values, OwnedRecordType.OwnerField), out var owner) ? owner : null,
                    Reference: Text(i.Values, "number"))),
            ],
            page.Value.TotalCount > items.Count);
    }

    public async Task<int?> CountOpenAsync(CustomerTimelineQuery query, CancellationToken cancellationToken)
    {
        if (query.SharedRecordScope is not { } scope)
        {
            return 0;
        }

        // Açık sipariş: taslak, onay bekleyen ve onaylanmış (iptal edilmiş ve kapanmış olanlar hariç).
        var filters = new Dictionary<string, string>
        {
            [scope.Field] = scope.Id.ToString(),
            ["status"] = string.Join(',', OrderStatus.Draft, OrderStatus.PendingApproval, OrderStatus.Approved),
        };
        var page = await pipeline.QueryAsync(new RecordQuery(OrdersModule.Entities.Order, 1, 1, Filters: filters), cancellationToken);
        return page.IsSuccess
            ? page.Value!.TotalCount
            : page.Error!.Code is RecordErrorCode.PermissionDenied or RecordErrorCode.FeatureNotLicensed
                ? null
                : throw new InvalidOperationException($"Açık iş sayılamadı ({Key}): {page.Error.Message}");
    }

    private static string? Text(JsonObject values, string name) => values[name]?.GetValue<string>();
}
