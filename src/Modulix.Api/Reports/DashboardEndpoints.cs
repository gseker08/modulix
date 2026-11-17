using System.Text.Json;
using Modulix.Api.Access;
using Modulix.Core.Modules;
using Modulix.Core.Reporting;
using Modulix.Shared.Reporting;

namespace Modulix.Api.Reports;

/// <summary>
/// Kullanıcı panoları (CRM epik E2, dilim 1). Panoyu açmak ve bileşen verisini okumak <c>core.reports.read</c>, pano oluşturmak, düzenlemek
/// ve silmek <c>core.reports.manage</c> ister. Pano yalnızca sahibine görünür; başkasının ya da başka firmanın panosu "bulunamadı" döner.
/// Bileşen verisi kayıtlı raporun çalıştırma sonucudur: çağıranın kayıt izni, veri kapsamı ve hassas alan kurallarıyla üretilir.
/// </summary>
public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboards").RequireAuthorization().WithTags("Dashboards");

        group.MapGet("/", async (IDashboards dashboards, CancellationToken ct) =>
            Results.Ok((await dashboards.ListAsync(ct)).Select(ToDto).ToList()))
            .RequirePermission(CoreModule.Permissions.ReadReports);

        group.MapGet("/{id:guid}", async (Guid id, IDashboards dashboards, CancellationToken ct) =>
            await dashboards.GetAsync(id, ct) is { } detail ? Results.Ok(ToDto(detail)) : NotFound())
            .RequirePermission(CoreModule.Permissions.ReadReports);

        group.MapPost("/", (SaveDashboardRequest request, IDashboards dashboards, CancellationToken ct) => RunAsync(async () =>
        {
            var detail = await dashboards.CreateAsync(ToInput(request), ct);
            return Results.Created($"/api/dashboards/{detail.Summary.Id}", ToDto(detail));
        })).RequirePermission(CoreModule.Permissions.ManageReports);

        group.MapPut("/{id:guid}", (Guid id, SaveDashboardRequest request, IDashboards dashboards, CancellationToken ct) => RunAsync(async () =>
            await dashboards.UpdateAsync(id, ToInput(request), request.ExpectedVersion, ct) is { } detail ? Results.Ok(ToDto(detail)) : NotFound()))
            .RequirePermission(CoreModule.Permissions.ManageReports);

        group.MapDelete("/{id:guid}", (Guid id, IDashboards dashboards, CancellationToken ct) => RunAsync(async () =>
            await dashboards.DeleteAsync(id, ct) ? Results.NoContent() : NotFound()))
            .RequirePermission(CoreModule.Permissions.ManageReports);

        // Bileşen verisi: bağlı raporun çalıştırma sonucu (durum `status` alanında, HTTP 200). Rapor çağırana görünmüyorsa 404.
        group.MapGet("/{id:guid}/widgets/{widgetId:guid}/data", async (Guid id, Guid widgetId, IDashboards dashboards, CancellationToken ct) =>
            await dashboards.RunWidgetAsync(id, widgetId, ct) is { } result ? Results.Ok(ReportEndpoints.ToDto(result)) : NotFound())
            .RequirePermission(CoreModule.Permissions.ReadReports);

        return app;
    }

    private static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DashboardException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.Field] = [ex.Message] }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (DashboardConflictException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
    }

    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Pano bulunamadı.");

    private static DashboardInput ToInput(SaveDashboardRequest request) =>
        new(request.Name, [.. (request.Widgets ?? []).Select(w => new DashboardWidgetInput(
            w.ReportId,
            // Tanımsız tür servis doğrulamasına bırakılır (alan hatası döner).
            Enum.TryParse<DashboardWidgetKind>(w.Kind, ignoreCase: true, out var kind) && Enum.IsDefined(kind) ? kind : 0,
            w.Title))]);

    private static string Name(Enum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static DashboardSummaryDto ToDto(DashboardSummary s) => new(s.Id, s.Name, s.WidgetCount, s.UpdatedAt, s.Version);

    private static DashboardDetailDto ToDto(DashboardDetail d) => new(
        ToDto(d.Summary), [.. d.Widgets.Select(w => new DashboardWidgetDto(w.Id, w.ReportId, w.ReportName, Name(w.Kind), w.Title, w.Position))]);
}
