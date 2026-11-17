using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulix.Core.Hosting;
using Modulix.Modules.Warehouse.Contracts.Customs;
using Modulix.Shared.Customs;
using Modulix.Shared.Warehouse;

namespace Modulix.Modules.Warehouse.Endpoints;

/// <summary>
/// Gümrük başvuru verisinin tenant uçları (ADR-0032 K9, K16; ADR-0046 Dilim 7). GTİP kataloğu ve rejim profilleri ürün standardı verisidir, tenant verisi
/// değildir; platformca yönetilir (<c>/api/platform/gtip</c>, <c>/api/platform/regime-profiles</c>), tenant yalnızca okur.
/// <list type="bullet">
///   <item><c>GET /api/warehouse/gtip/check</c>, <c>/gtip/search</c>: stok kartı formu kodu yazarken denetler (stok kartı okuma izni). Katalogda olmayan kod
///   yalnızca uyarıdır, hiçbir kaydı engellemez.</item>
///   <item><c>GET /api/warehouse/regime/profiles</c>: yayındaki etkin profiller (rejim bağlaması okuma izni).</item>
///   <item><c>GET /api/warehouse/regime/check</c>: beyannamenin tescil numarasını rejiminin profiline göre denetler (beyanname okuma izni). Denetim yalnızca
///   bilgidir; engel düzeyi kayıt boru hattında uygulanır.</item>
/// </list>
/// </summary>
internal static class CustomsEndpoints
{
    /// <summary><paramref name="routes"/> host tarafından <c>/api/warehouse</c> grubu olarak, oturum ve modül kontrolüyle sarılmış gelir (ADR-0046).</summary>
    public static void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var gtip = routes.MapGroup("/gtip")
            .RequirePermission(WarehouseAccess.StockItemsRead)
            .WithTags("Warehouse");
        gtip.MapGet("/check", async (string? code, DateOnly? date, IGtipCatalog catalog, TimeProvider time, CancellationToken ct) =>
        {
            var (value, error) = GtipRules.Normalize(code);
            if (value is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: error ?? "GTİP kodu boş olamaz.");
            }

            var check = await catalog.CheckAsync(value, date ?? Today(time), ct);
            return Results.Ok(new GtipCheckDto(check.Status.ToString(), check.Description, check.VersionName));
        });
        gtip.MapGet("/search", async (string? q, DateOnly? date, int? limit, IGtipCatalog catalog, TimeProvider time, CancellationToken ct) =>
            (await catalog.SearchAsync(q ?? string.Empty, date ?? Today(time), limit ?? 10, ct))
                .Select(s => new GtipEntryDto(s.Code, s.Description, s.Unit)).ToList());

        var regime = routes.MapGroup("/regime").WithTags("Warehouse");
        regime.MapGet("/profiles", async (IRegimeProfileCatalog catalog, CancellationToken ct) =>
                (await catalog.ListPublishedAsync(ct)).Select(p => new PublishedRegimeProfileDto(
                    p.ProfileId, p.Code, p.Name, p.VersionNumber, p.IsVerified, p.VerificationNote,
                    p.Content.RegistrationNo is { } r ? new RegistrationNoRuleDto(r.Pattern, r.MinLength, r.MaxLength, r.Example, r.Enforcement.ToString()) : null)).ToList())
            .RequirePermission(WarehouseAccess.RegimesRead);

        regime.MapGet("/check", async (string? regimeKey, string? registrationNo, IRegistrationNoCheck check, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(regimeKey) || string.IsNullOrWhiteSpace(registrationNo))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Rejim ve tescil numarası gerekli.");
                }

                var result = await check.CheckAsync(regimeKey.Trim(), registrationNo.Trim(), ct);
                return Results.Ok(new RegistrationNoCheckDto(result.HasRule, result.Enforcement, [.. result.Violations], result.ProfileName, result.VersionNumber, result.Verified));
            })
            .RequirePermission(WarehouseAccess.DeclarationsRead);
    }

    private static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
}
