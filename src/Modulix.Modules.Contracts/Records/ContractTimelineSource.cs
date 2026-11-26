using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Persistence;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Contracts.Domain;
using Modulix.Modules.Crm.Contracts.Timeline;

namespace Modulix.Modules.Contracts.Records;

/// <summary>
/// Müşteri zaman çizelgesinin sözleşme kaynağı (ADR-0053, dilim 2): müşterinin sözleşmeleri, oluşturulma anıyla. Sözleşmeler ortak taraf kaydına
/// (<c>partyId</c>) bağlıdır; satırlar kayıt boru hattından okunur, izin, veri kapsamı, gizlilik ve lisans orada uygulanır. Durum olarak
/// yürürlük durumu (<c>effectiveStatus</c>) verilir. Boru hattı oluşturulma anını dışarı vermediği için o an, boru hattının görünür bulduğu
/// kimliklerle modülün kendi tablosundan okunur.
/// </summary>
internal sealed class ContractTimelineSource(IRecordPipeline pipeline, IModuleData data) : ICustomerTimelineSource
{
    public bool SupportsPerson => true;

    public string Key => "contracts.contracts";

    public async Task<CustomerTimelineSlice?> ReadAsync(CustomerTimelineQuery query, CancellationToken cancellationToken)
    {
        if (query.SharedRecordScope is not { } scope)
        {
            // Ortak taraf kaydı olmayan firmanın sözleşmesi olamaz: okunabilir, boş kaynak.
            return new CustomerTimelineSlice([], false);
        }

        var filters = new Dictionary<string, string> { [scope.Field] = scope.Id.ToString() };
        if (query.Before is { } before)
        {
            filters["createdBefore"] = before.ToString("O", CultureInfo.InvariantCulture);
        }

        var take = Math.Clamp(query.Take, 1, RecordQuery.MaxPageSize);
        var page = await pipeline.QueryAsync(new RecordQuery(ContractsModule.Entities.Contract, 1, take, Filters: filters), cancellationToken);
        if (!page.IsSuccess)
        {
            // Çağıran sözleşmeleri okuyamıyorsa kaynak yoktur; başka hata sessizce boş çizelgeye çevrilmez.
            return page.Error!.Code is RecordErrorCode.PermissionDenied or RecordErrorCode.FeatureNotLicensed
                ? null
                : throw new InvalidOperationException($"Zaman çizelgesi okunamadı ({Key}): {page.Error.Message}");
        }

        var items = page.Value!.Items;
        var ids = items.Select(i => i.Id).ToList();
        var created = await data.Set<Contract>().AsNoTracking().Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.CreatedAt, cancellationToken);
        return new CustomerTimelineSlice(
            [
                .. items.Select(i => new CustomerTimelineEntry(
                    ContractsModule.Entities.Contract,
                    i.Id,
                    created[i.Id],
                    Text(i.Values, "title") ?? string.Empty,
                    Text(i.Values, "effectiveStatus"),
                    Text(i.Values, "type"),
                    Guid.TryParse(Text(i.Values, OwnedRecordType.OwnerField), out var owner) ? owner : null,
                    Text(i.Values, "number"))),
            ],
            page.Value.TotalCount > items.Count);
    }

    private static string? Text(JsonObject values, string name) => values[name]?.GetValue<string>();
}
