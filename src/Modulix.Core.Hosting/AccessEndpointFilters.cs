using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Authorization;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Tenancy;

namespace Modulix.Core.Hosting;

public static class AccessEndpointFilters
{
    /// <summary>
    /// Aktif tenant ve izin ister; yoksa 403. İznin modülü tenant için kapalıysa izin rolde olsa bile 403.
    /// Modül uç noktaları bunu kullanır: <c>.RequirePermission(CrmModule.Permissions.CompaniesRead)</c>.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        builder.AddEndpointFilter<TBuilder, ActiveTenantFilter>();
        builder.AddEndpointFilter(async (context, next) =>
        {
            var access = context.HttpContext.RequestServices.GetRequiredService<ICurrentAccess>();
            return await access.HasPermissionAsync(permission, context.HttpContext.RequestAborted)
                ? await next(context)
                : Forbidden($"Bu işlem için '{permission}' izni gerekir.");
        });
        return builder;
    }

    /// <summary>Aktif tenant ve verilen izinlerden en az biri; yoksa 403.</summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        if (permissions.Length == 0 || permissions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("En az bir izin gerekli.", nameof(permissions));
        }

        builder.AddEndpointFilter<TBuilder, ActiveTenantFilter>();
        builder.AddEndpointFilter(async (context, next) =>
        {
            var access = context.HttpContext.RequestServices.GetRequiredService<ICurrentAccess>();
            var granted = await access.GetPermissionsAsync(context.HttpContext.RequestAborted);
            return permissions.Any(granted.Contains)
                ? await next(context)
                : Forbidden($"Bu işlem için şu izinlerden biri gerekir: {string.Join(", ", permissions)}.");
        });
        return builder;
    }

    /// <summary>
    /// Aktif tenant ister ve modül tenant için kapalıysa 403 döner. Modülün lisans süresi ve ek süresi bittiyse (salt okunur,
    /// ADR-0028) okuma yöntemleri (GET, HEAD, OPTIONS) ve yol adı <c>/preview</c> ile biten önizlemeler çalışır, diğerleri 403
    /// döner; veri silinmez. Önizleme uç noktaları (POST olsa da) hiçbir şey yazmaz; yeni bir salt okunur POST ucu bu adla bitmelidir.
    /// </summary>
    public static TBuilder RequireModule<TBuilder>(this TBuilder builder, string moduleKey)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        builder.AddEndpointFilter<TBuilder, ActiveTenantFilter>();
        builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var access = services.GetRequiredService<ICurrentAccess>();
            if (!await access.IsModuleEnabledAsync(moduleKey, context.HttpContext.RequestAborted))
            {
                return Forbidden($"'{moduleKey}' modülü bu tenant için açık değil.");
            }

            if (!IsReadOnlySafe(context.HttpContext.Request)
                && !await services.GetRequiredService<IModuleWriteAccess>().IsWritableAsync(
                    services.GetRequiredService<ITenantContextAccessor>().Current!.TenantId, moduleKey, context.HttpContext.RequestAborted))
            {
                return Forbidden($"'{moduleKey}' modülünün lisans süresi doldu; modül salt okunur. Yöneticinizle iletişime geçin.");
            }

            return await next(context);
        });
        return builder;
    }

    private static bool IsReadOnlySafe(HttpRequest request) =>
        HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)
        || request.Path.Value?.TrimEnd('/').EndsWith("/preview", StringComparison.OrdinalIgnoreCase) == true;

    private static IResult Forbidden(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Yetkiniz yok.", detail: detail);
}
