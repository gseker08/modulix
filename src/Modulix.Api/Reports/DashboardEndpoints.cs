using System.Text.Json;
using Modulix.Api.Access;
using Modulix.Core.Modules;
using Modulix.Core.Reporting;
using Modulix.Shared.Reporting;

namespace Modulix.Api.Reports;

/// <summary>
/// Kullanıcı panoları (CRM epik E2). Panoyu açmak, bileşen verisini okumak ve kendi varsayılanını seçmek <c>core.reports.read</c>, pano
/// oluşturmak, düzenlemek ve silmek <c>core.reports.manage</c>, paylaşmak <c>core.reports.share</c> ister. Pano sahibine, paylaşıldığı
/// kullanıcılara ya da firmaya görünür; görünmeyen ya da başka firmanın panosu "bulunamadı", görünen ama başkasının panosunu değiştirmek 403 döner.
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

        group.MapPut("/{id:guid}/sharing", (Guid id, ShareDashboardRequest request, IDashboards dashboards, CancellationToken ct) => RunAsync(async () =>
        {
            if (!Enum.TryParse<ReportVisibility>(request.Visibility, ignoreCase: true, out var visibility) || !Enum.IsDefined(visibility))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["visibility"] = ["Paylaşım private, users ya da tenant olmalı."] }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!ShareGroupMapping.TryRead(request.Groups, out var groups))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["groups"] = ["Paylaşım grubu rol, ekip ya da iş birimi olmalı."] }, statusCode: StatusCodes.Status400BadRequest);
            }

            return await dashboards.ShareAsync(id, visibility, request.Users ?? [], groups, request.ExpectedVersion, ct) is { } detail ? Results.Ok(ToDto(detail)) : NotFound();
        })).RequirePermission(CoreModule.Permissions.ShareReports);

        // Varsayılan pano kişiye özeldir: kullanıcı görebildiği bir panoyu kendi varsayılanı yapar (panoyu değiştirmez).
        group.MapPut("/{id:guid}/default", (Guid id, DefaultDashboardRequest request, IDashboards dashboards, CancellationToken ct) => RunAsync(async () =>
            await dashboards.SetDefaultAsync(id, request.IsDefault, ct) is { } isDefault ? Results.Ok(new DefaultDashboardResponse(isDefault)) : NotFound()))
            .RequirePermission(CoreModule.Permissions.ReadReports);

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
        catch (DashboardForbiddenException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: ex.Message);
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

    private static DashboardSummaryDto ToDto(DashboardSummary s) => new(
        s.Id, s.Name, s.WidgetCount, s.UpdatedAt, s.Version, Name(s.Visibility), s.OwnerUserId, s.OwnerName, s.IsOwner, s.IsDefault, s.SharedWithCount);

    private static DashboardDetailDto ToDto(DashboardDetail d) => new(
        ToDto(d.Summary), [.. d.Widgets.Select(w => new DashboardWidgetDto(w.Id, w.ReportId, w.ReportName, Name(w.Kind), w.Title, w.Position))], d.SharedWith, ShareGroupMapping.ToDto(d.SharedWithGroups));
}
