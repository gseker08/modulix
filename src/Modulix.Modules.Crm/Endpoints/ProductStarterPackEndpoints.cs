using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulix.Core.Contracts.Customization;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Hosting;
using Modulix.Modules.Crm.Domain;
using Modulix.Shared.Crm;

namespace Modulix.Modules.Crm.Endpoints;

/// <summary>
/// Sektör profilinin önerdiği katalog kalemlerinin yüklenmesi (ör. lojistikte masraf kalemleri). Kalemler profil metadata'sından gelir
/// (<see cref="ISectorCatalogSuggestions"/>); CRM sektör bilmez. Yükleme çağıranın izniyle kayıt boru hattından geçer (izin, denetim, olay);
/// aynı kodlu ürün atlanır, yani tekrar çalıştırmak güvenlidir. Tutar girilmez: fiyatı firma fiyat listesinde verir.
/// </summary>
internal static class ProductStarterPackEndpoints
{
    private const string DefaultUnitCode = "ADET";

    public static void Map(RouteGroupBuilder crm)
    {
        crm.MapGet("/products/starter-pack", PreviewAsync).RequirePermission(CrmModule.Permissions.ProductsRead);
        crm.MapPost("/products/starter-pack", ApplyAsync).RequirePermission(CrmModule.Permissions.ProductsWrite);
    }

    private static async Task<IResult> PreviewAsync(ISectorCatalogSuggestions suggestions, IRecordPipeline pipeline, CancellationToken ct)
    {
        var items = await suggestions.ListAsync(ct);
        var rows = new List<CatalogStarterItemDto>();
        foreach (var item in items)
        {
            var exists = await ExistsAsync(pipeline, item.Code, ct);
            if (exists.Error is not null)
            {
                return RecordProblems.ToProblem(exists.Error);
            }

            rows.Add(new CatalogStarterItemDto(item.Code, item.Name, item.Kind, exists.Value));
        }

        return Results.Ok(new CatalogStarterPackDto(rows, rows.Count(r => !r.Exists)));
    }

    private static async Task<IResult> ApplyAsync(ISectorCatalogSuggestions suggestions, IRecordPipeline pipeline, CancellationToken ct)
    {
        var items = await suggestions.ListAsync(ct);
        var missing = new List<CatalogSuggestion>();
        foreach (var item in items)
        {
            var exists = await ExistsAsync(pipeline, item.Code, ct);
            if (exists.Error is not null)
            {
                return RecordProblems.ToProblem(exists.Error);
            }

            if (!exists.Value)
            {
                missing.Add(item);
            }
        }

        if (missing.Count == 0)
        {
            return Results.Ok(new CatalogStarterPackResultDto(0, items.Count));
        }

        var unit = await EnsureUnitAsync(pipeline, ct);
        if (unit.Error is not null)
        {
            return RecordProblems.ToProblem(unit.Error);
        }

        foreach (var item in missing)
        {
            var created = await pipeline.CreateAsync(new RecordCommand(CrmModule.Entities.Product, null, new JsonObject
            {
                ["code"] = item.Code,
                ["name"] = item.Name,
                ["description"] = item.Description,
                ["kind"] = item.Kind,
                ["status"] = CatalogRules.StatusActive,
                ["baseUnitId"] = unit.Value.ToString(),
            }), ct);
            if (!created.IsSuccess)
            {
                return RecordProblems.ToProblem(created.Error!);
            }
        }

        return Results.Created("/api/crm/products", new CatalogStarterPackResultDto(missing.Count, items.Count - missing.Count));
    }

    private static async Task<(bool Value, RecordError? Error)> ExistsAsync(IRecordPipeline pipeline, string code, CancellationToken ct)
    {
        var page = await pipeline.QueryAsync(new RecordQuery(CrmModule.Entities.Product, 1, 1, null, new Dictionary<string, string> { ["code"] = code }), ct);
        return page.IsSuccess ? (page.Value!.TotalCount > 0, null) : (false, page.Error);
    }

    // Temel birim: katsayısı 1 olan ilk "adet" grubu birimi; yoksa ADET açılır.
    private static async Task<(Guid Value, RecordError? Error)> EnsureUnitAsync(IRecordPipeline pipeline, CancellationToken ct)
    {
        var units = await pipeline.QueryAsync(new RecordQuery(CrmModule.Entities.MeasureUnit, 1, RecordQuery.MaxPageSize), ct);
        if (!units.IsSuccess)
        {
            return (Guid.Empty, units.Error);
        }

        var existing = units.Value!.Items.FirstOrDefault(u => u.Values["groupKey"]?.GetValue<string>() == "count" && u.Values["factorToBase"]?.GetValue<decimal>() == 1m);
        if (existing is not null)
        {
            return (existing.Id, null);
        }

        var created = await pipeline.CreateAsync(new RecordCommand(CrmModule.Entities.MeasureUnit, null, new JsonObject
        {
            ["code"] = DefaultUnitCode,
            ["name"] = "Adet",
            ["groupKey"] = "count",
            ["factorToBase"] = 1m,
        }), ct);
        return created.IsSuccess ? (created.Value!.Id, null) : (Guid.Empty, created.Error);
    }
}
