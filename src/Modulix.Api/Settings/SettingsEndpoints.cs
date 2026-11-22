using Modulix.Api.Access;
using Modulix.Api.Records;
using Modulix.Api.Tenancy;
using Modulix.Api.Users;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Modules;
using Modulix.Core.Settings;
using Modulix.Shared.Settings;

namespace Modulix.Api.Settings;

/// <summary>
/// Kuruluş ve modül ayarları (Y6): /api/settings. Okuma her üyeye açıktır (ekranlar saat dilimine, para birimine bakar); yazma
/// <c>core.settings.manage</c> ister. Kurallar <see cref="ITenantSettings"/>'tadır.
/// </summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTenantFilter>()
            .WithTags("Settings");

        settings.MapGet("/", async (ITenantSettings tenantSettings, ITenantLogo logo, IRequestLanguage language, ITextCatalog texts, CancellationToken ct) =>
            ToResponse(await tenantSettings.GetAsync(ct), await logo.GetInfoAsync(ct), await BundleAsync(language, texts, ct)));
        settings.MapPut("/", SaveAsync).RequirePermission(CoreModule.Permissions.ManageSettings);

        // Vurgu rengi önizlemesi (E8): kaydetmeden önce kontrast sonucu. Hiçbir şey yazmaz; yolu /preview ile biter.
        settings.MapPost("/accent/preview", (AccentPreviewRequest request) =>
        {
            var check = AccentColor.Check((request.Color ?? string.Empty).Trim(), request.Dark);
            return Results.Ok(new AccentPreviewResponse(check.Valid, check.Contrast, AccentColor.MinContrast, check.Message));
        }).RequirePermission(CoreModule.Permissions.ManageSettings);

        // Logo (Y6b): gövde dosyanın kendisidir (PNG veya JPEG, en fazla 256 KB); tür dosyanın içeriğinden belirlenir.
        settings.MapGet("/logo", GetLogoAsync);
        settings.MapPut("/logo", SaveLogoAsync).RequirePermission(CoreModule.Permissions.ManageSettings);
        settings.MapDelete("/logo", async (ITenantLogo logo, CancellationToken ct) =>
        {
            await logo.DeleteAsync(ct);
            return Results.NoContent();
        }).RequirePermission(CoreModule.Permissions.ManageSettings);

        return app;
    }

    private static async Task<IResult> GetLogoAsync(HttpContext http, ITenantLogo logo, CancellationToken ct)
    {
        if (await logo.GetAsync(ct) is not { } found)
        {
            return Results.NotFound();
        }

        // Adres sürümü taşır (?v=); sürüm değişince adres de değişir. Tarayıcı türü tahmin etmesin.
        http.Response.Headers.CacheControl = "private, max-age=86400";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
        return Results.File(found.Data, found.Info.ContentType);
    }

    private static async Task<IResult> SaveLogoAsync(HttpRequest request, ITenantLogo logo, CancellationToken ct)
    {
        // Sınırın bir bayt fazlası okunur: daha büyük dosya baştan reddedilir.
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > TenantLogo.MaxBytes)
            {
                break;
            }
        }

        try
        {
            var info = await logo.SaveAsync(buffer.ToArray(), ct);
            return Results.Ok(ToDto(info));
        }
        catch (ArgumentException ex)
        {
            return UserEndpoints.ToProblem(ex)!;
        }
    }

    private static async Task<IResult> SaveAsync(
        SaveSettingsRequest request, ITenantSettings tenantSettings, ITenantLogo logo, IRequestLanguage language, ITextCatalog texts, CancellationToken ct)
    {
        try
        {
            var values = (request.Values ?? new Dictionary<string, SettingWriteDto>())
                .ToDictionary(v => v.Key, v => new SettingWrite(v.Value?.Value ?? string.Empty, v.Value?.Version ?? 0), StringComparer.Ordinal);
            var result = await tenantSettings.SaveAsync(values, ct);
            return result.IsSuccess
                ? Results.Ok(ToResponse(result.Value!, await logo.GetInfoAsync(ct), await BundleAsync(language, texts, ct)))
                : RecordEndpoints.ToProblem(result.Error!);
        }
        catch (ArgumentException ex)
        {
            return UserEndpoints.ToProblem(ex)!;
        }
    }

    private static TenantLogoDto ToDto(TenantLogoInfo info) => new(info.ContentType, info.Size, info.Version);

    private static async ValueTask<IReadOnlyDictionary<string, string>> BundleAsync(IRequestLanguage language, ITextCatalog texts, CancellationToken ct) =>
        texts.Bundle(await language.GetAsync(ct));

    // Ayarın adı, bölümü, açıklaması ve seçenek adları okurun dilinde (ADR-0034, dilim 18c-3); anahtar, değer ve varsayılan değer değişmez.
    private static SettingsResponse ToResponse(IReadOnlyList<TenantSettingValue> values, TenantLogoInfo? logo, IReadOnlyDictionary<string, string> bundle) =>
        new([.. values.Select(v => new SettingDto(
            v.Definition.Key,
            v.ModuleKey,
            MetadataTexts.Text(bundle, MetadataTexts.SettingGroupKey(v.Definition.Key), v.Definition.Group),
            MetadataTexts.Text(bundle, MetadataTexts.SettingKey(v.Definition.Key), v.Definition.DisplayName),
            v.Definition.Description is null ? null : MetadataTexts.Text(bundle, MetadataTexts.SettingDescriptionKey(v.Definition.Key), v.Definition.Description),
            (SettingKind)v.Definition.Type,
            v.Definition.MaxLength,
            v.Definition.Required,
            [.. v.Definition.Options.Select(o => new SettingOptionDto(o.Value, MetadataTexts.Text(bundle, MetadataTexts.SettingOptionKey(v.Definition.Key, o.Value), o.Label)))],
            v.Definition.DefaultValue,
            v.Value,
            v.Version))], logo is null ? null : ToDto(logo));
}
