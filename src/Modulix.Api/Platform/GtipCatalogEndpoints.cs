using System.Security.Claims;
using Modulix.Api.Imports;
using Modulix.Api.Users;
using Modulix.Modules.Warehouse.Contracts.Customs;
using Modulix.Shared.Customs;

namespace Modulix.Api.Platform;

/// <summary>
/// GTİP kataloğu yönetimi (ADR-0032 K9): platform yöneticisine açıktır (<c>/api/platform/gtip</c>, tenant gerekmez). Katalog tabloları depo modülünün
/// başvuru tablolarıdır (ADR-0046 bölüm 6); platform uçları host'ta kalır (ADR-0046 karar 4), uygulama modüldedir. Tenant'ın okuma uçları modüldedir
/// (<c>/api/warehouse/gtip</c>).
/// </summary>
public static class GtipCatalogEndpoints
{
    public static IEndpointRouteBuilder MapGtipCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var platform = app.MapGroup("/api/platform/gtip")
            .RequireAuthorization()
            .AddEndpointFilter<RequirePlatformAdministratorFilter>()
            .WithTags("Platform");
        platform.MapGet("/versions", async (IGtipCatalogAdmin catalog, CancellationToken ct) =>
            (await catalog.ListVersionsAsync(ct)).Select(ToDto).ToList());
        platform.MapGet("/versions/{id:guid}/entries", (Guid id, string? search, int? page, int? pageSize, string? sort, IGtipCatalogAdmin catalog, CancellationToken ct) =>
            Run(async () =>
            {
                // Başlığa göre sıralama (ADR-0036): yalnızca bilinen anahtarlar kabul edilir.
                if (!string.IsNullOrEmpty(sort) && !GtipEntrySort.Keys.Contains(sort))
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["sort"] = ["Geçersiz sıralama."] }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                var result = await catalog.SearchEntriesAsync(id, search, page ?? 1, pageSize ?? 50, ct, string.IsNullOrEmpty(sort) ? null : sort);
                return Results.Ok(new GtipEntryPageDto([.. result.Items.Select(e => new GtipEntryDto(e.Code, e.Description, e.Unit))], result.TotalCount, result.Page, result.PageSize));
            }));

        // Dosya (Excel/CSV) gövdede ham bayttır; ad ve sürüm bilgisi sorgudadır. Önizleme hiçbir şey yazmaz (yolu /preview ile biter).
        platform.MapPost("/versions/preview", async (string? fileName, HttpRequest http, IGtipFileReader reader, CancellationToken ct) =>
        {
            var (content, problem) = await LimitedBody.ReadAsync(http, GtipImportRules.MaxFileBytes, ct);
            if (content is null)
            {
                return problem!;
            }

            var read = reader.Read(content, fileName);
            return Results.Ok(new GtipImportPreviewDto(
                read.FatalError, read.CanImport, read.RowsRead, read.Entries.Count, read.HeadingRows, read.PaddedCodes, read.ErrorCount,
                [.. read.Errors.Select(e => new GtipImportErrorDto(e.Row, e.Message))],
                [.. read.Entries.Take(10).Select(e => new GtipEntryDto(e.Code, e.Description, e.Unit))]));
        });
        platform.MapPost("/versions", async (string? fileName, string? name, DateOnly? effectiveFrom, ClaimsPrincipal user, HttpRequest http, IGtipFileReader reader, IGtipCatalogAdmin catalog, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name) || effectiveFrom is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Sürüm adı ve yürürlük tarihi (yyyy-aa-gg) gerekli.");
            }

            var (content, problem) = await LimitedBody.ReadAsync(http, GtipImportRules.MaxFileBytes, ct);
            if (content is null)
            {
                return problem!;
            }

            // Hatalı dosya kısmen yüklenmez: eksik katalog sessizce yürürlüğe girmesin.
            var read = reader.Read(content, fileName);
            if (!read.CanImport)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: read.FatalError ?? (read.ErrorCount > 0 ? $"Dosyada {read.ErrorCount} hatalı satır var; önizlemeden düzeltip yeniden yükleyin." : "Dosyada içe aktarılacak GTİP satırı yok."));
            }

            return await Run(async () =>
            {
                var created = await catalog.CreateVersionAsync(PlatformEndpoints.UserId(user), new GtipCatalogVersionInput(name, effectiveFrom.Value, fileName), read.Entries, ct);
                return Results.Created($"/api/platform/gtip/versions/{created.Id}", ToDto(created));
            });
        });
        platform.MapDelete("/versions/{id:guid}",(Guid id, ClaimsPrincipal user, IGtipCatalogAdmin catalog, CancellationToken ct) =>
            Run(async () =>
            {
                await catalog.DeleteVersionAsync(PlatformEndpoints.UserId(user), id, ct);
                return Results.NoContent();
            }));

        return app;
    }

    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (GtipCatalogNotFoundException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: ex.Message);
        }
        catch (GtipCatalogConflictException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
        catch (ArgumentException ex)
        {
            return UserEndpoints.ToProblem(ex)!;
        }
    }

    private static GtipVersionDto ToDto(GtipCatalogVersionView v) => new(v.Id, v.Name, v.EffectiveFrom, v.SourceFileName, v.EntryCount, v.CreatedAt);
}
