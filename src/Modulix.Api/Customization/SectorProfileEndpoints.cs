using Modulix.Api.Access;
using Modulix.Api.Tenancy;
using Modulix.Api.Users;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Customization;
using Modulix.Core.Modules;
using Modulix.Shared.Customization;

namespace Modulix.Api.Customization;

/// <summary>
/// Sektör profilleri (Y9c), firma yöneticisi için: <c>/api/customization/profiles</c>, <c>core.customization.manage</c> ister.
/// Firma yöneticisi modül açamaz; kapalı modülün alanları ve değerleri eklenmez (önizlemede ayrıca gösterilir). Platform
/// yöneticisinin uçları <c>/api/platform</c> altındadır.
/// </summary>
public static class SectorProfileEndpoints
{
    public static IEndpointRouteBuilder MapSectorProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup("/api/customization/profiles")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTenantFilter>()
            .RequirePermission(CoreModule.Permissions.ManageCustomization)
            .WithTags("Customization");
        profiles.MapGet("/", async (ISectorProfileService service, SectorProfileCatalog catalog, CancellationToken ct) =>
        {
            var current = await service.GetAsync(ct);
            return new SectorProfilesResponse(current.ProfileKey, current.AppliedAt, ToDto(catalog));
        });
        profiles.MapGet("/{key}/preview", async (string key, ISectorProfileService service, ModuleCatalog modules, IRequestLanguage language, ITextCatalog texts, CancellationToken ct) =>
            await service.PreviewAsync(key, enableModules: false, ct) is { } plan ? Results.Ok(ToDto(plan, modules, texts.Bundle(await language.GetAsync(ct)))) : NotFound());
        profiles.MapPost("/{key}/apply", async (string key, ISectorProfileService service, ModuleCatalog modules, IRequestLanguage language, ITextCatalog texts, CancellationToken ct) =>
        {
            try
            {
                return await service.ApplyAsync(key, enableModules: false, ct) is { } plan ? Results.Ok(ToDto(plan, modules, texts.Bundle(await language.GetAsync(ct)))) : NotFound();
            }
            catch (ArgumentException ex)
            {
                return UserEndpoints.ToProblem(ex)!;
            }
        });

        return app;
    }

    internal static List<SectorProfileDto> ToDto(SectorProfileCatalog catalog) =>
        [.. catalog.Profiles.Select(p => new SectorProfileDto(p.Key, p.Name, p.Description, p.Modules))];

    /// <summary>Plan önizlemesi; modül, kayıt türü ve liste adları <paramref name="bundle"/> dilinde (ADR-0034, dilim 18c-5). Profilin kendi adı ve eklediği alan ve değer adları profil verisidir, çevrilmez.</summary>
    internal static ProfilePlanDto ToDto(ProfilePlan plan, ModuleCatalog modules, IReadOnlyDictionary<string, string> bundle) => new(
        plan.Profile.Key,
        plan.Profile.Name,
        [.. plan.ModulesToEnable.Select(m => modules.Find(m) is { } module ? MetadataTexts.Text(bundle, MetadataTexts.ModuleKey(m), module.DisplayName) : m)],
        [.. plan.UnavailableModules.Select(m => modules.Find(m) is { } module ? MetadataTexts.Text(bundle, MetadataTexts.ModuleKey(m), module.DisplayName) : m)],
        [.. plan.Fields.Select(f => new ProfileFieldDto(
            modules.FindCustomizable(f.EntityKey) is { } entity
                ? MetadataTexts.Text(bundle, MetadataTexts.CustomizableKey(f.EntityKey), entity.Entity.DisplayName)
                : f.EntityKey,
            f.Label, (Shared.Customization.CustomFieldType)f.Type, f.Options ?? []))],
        [.. plan.ListValues.Select(v => new ProfileListValueDto(modules.FindChoiceList(v.ListKey) is { } list ? MetadataTexts.Text(bundle, MetadataTexts.ChoiceListKey(v.ListKey), list.List.DisplayName) : v.ListKey, v.Label))],
        plan.IsEmpty);

    internal static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Sektör profili bulunamadı.");
}
