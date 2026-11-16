using Modulix.Api.Tenancy;
using Modulix.Core.Contracts.Numbering;
using Modulix.Core.Modules;
using Modulix.Core.Numbering;
using Modulix.Shared.Numbering;

namespace Modulix.Api.Numbering;

/// <summary>
/// Belge numarası serileri (ADR-0049 dilim 2a): /api/numbering/series. Firma önek, basamak ve sıfırlamayı değiştirir, sayacı ileri alır.
/// Hepsi <c>core.numbering.manage</c> ister; değişiklikler denetim kaydına yazılır. Kurallar <see cref="INumberSeriesAdmin"/>'dedir.
/// </summary>
public static class NumberingEndpoints
{
    public static IEndpointRouteBuilder MapNumberingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/numbering/series")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTenantFilter>()
            .RequirePermission(CoreModule.Permissions.ManageNumbering)
            .WithTags("Numbering");

        group.MapGet("/", async (INumberSeriesAdmin admin, CancellationToken ct) =>
            Results.Ok((await admin.ListAsync(ct)).Select(ToDto).ToList()));

        group.MapPut("/{key}", (string key, ConfigureNumberSeriesRequest request, INumberSeriesAdmin admin, CancellationToken ct) => RunAsync(async () =>
        {
            if (!Enum.TryParse(request.Reset, ignoreCase: false, out NumberSeriesReset reset) || !Enum.IsDefined(reset))
            {
                return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Sıfırlama \"Never\" ya da \"Yearly\" olmalı.");
            }

            return await admin.ConfigureAsync(key, request.Prefix, request.Digits, reset, request.Version, ct) is { } view ? Results.Ok(ToDto(view)) : NotFound();
        }));

        group.MapPost("/{key}/advance", (string key, AdvanceNumberSeriesRequest request, INumberSeriesAdmin admin, CancellationToken ct) => RunAsync(async () =>
            await admin.AdvanceAsync(key, request.NextValue, request.Version, ct) is { } view ? Results.Ok(ToDto(view)) : NotFound()));

        return app;
    }

    private static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (NumberSeriesConflictException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
        catch (NumberSeriesException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: ex.Message);
        }
    }

    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Belge serisi bulunamadı.");

    private static NumberSeriesDto ToDto(NumberSeriesView view) => new(
        view.Declared.Key, view.ModuleKey, view.Declared.DisplayName,
        view.Effective.Prefix, view.Effective.Digits, view.Effective.Reset.ToString(),
        view.Declared.Prefix, view.Declared.Digits, view.Declared.Reset.ToString(),
        view.IsCustomized, view.Year, view.LastValue, view.NextNumber, view.Version);
}
