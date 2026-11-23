using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Api.Localization;
using Modulix.Api.Platform;
using Modulix.Core.Modules;
using Modulix.Core.Platform;
using Modulix.Core.Records;
using Modulix.Core.Settings;
using Modulix.Infrastructure.Authorization;
using Modulix.Infrastructure.Persistence;
using Modulix.Infrastructure.Platform;
using Modulix.Shared.Auth;

namespace Modulix.Api.Tenancy;

public static class ModulixClaimTypes
{
    /// <summary>Aktif tenant. Yalnızca sunucu yazar (şifreli oturum çerezinde); her istekte yeniden doğrulanır.</summary>
    public const string TenantId = "modulix:tenant_id";

    /// <summary>Sunucudaki oturum (Y7a, <c>platform.UserSessions</c>); her istekte kapatılmadığı denetlenir.</summary>
    public const string SessionId = "modulix:session_id";

    /// <summary>
    /// Aktif işletici şirket (ADR-0008, adım 2). Yalnızca kişinin açık seçimini taşır; yoksa varsayılan şirket her istekte
    /// hesaplanır. Yalnızca sunucu yazar (şifreli oturum çerezinde) ve her istekte erişilebilir şirketlere karşı doğrulanır.
    /// </summary>
    public const string CompanyId = "modulix:company_id";
}

/// <summary>İstek başına aktif tenant bağlamı; <see cref="TenantContextMiddleware"/> doldurur.</summary>
public sealed class HttpTenantContextAccessor : ITenantContextAccessor
{
    public TenantContext? Current { get; set; }

    /// <summary>
    /// Oturumdaki firma iki adımlı doğrulamayı zorunlu kılıyor ve kişinin doğrulaması kapalı (Y7b): bağlam oluşmaz,
    /// firma uç noktaları 403 ve <see cref="TenantProblems.TwoFactorRequired"/> döner; kişi önce kurulumu yapar.
    /// </summary>
    public Guid? TwoFactorRequiredTenantId { get; set; }
}

public static class TenantProblems
{
    /// <summary>403 sorun ayrıntısının <c>code</c> alanı: firma iki adımlı doğrulama istiyor.</summary>
    public const string TwoFactorRequired = "two-factor-required";
}

/// <summary>
/// Oturumdaki tenant iddiasını her istekte veritabanından doğrular. Üyelik devre dışı bırakılır
/// veya tenant kapatılırsa bağlam oluşmaz ve tenant gerektiren uç noktalar 403 döner.
/// İstemcinin gönderdiği bir başlık veya parametre tenant belirlemez.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context, HttpTenantContextAccessor accessor, IMembershipDirectory directory, ModulixDbContext db, AccessCache cache)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated == true
            && Guid.TryParse(principal.FindFirst(ModulixClaimTypes.TenantId)?.Value, out var tenantId)
            && Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            var membership = await directory.FindActiveAsync(userId, tenantId, context.RequestAborted);
            if (membership is not null)
            {
                accessor.Current = new TenantContext(membership.TenantId, userId, membership.MembershipId);

                // Tenant artık bu istek boyunca değişmez: bağlantı bir kez açılır ve istek sonuna kadar açık kalır. Açılışta interceptor
                // tenant'ı oturuma salt okunur yazar; sonraki sorgular aynı bağlantıyı kullanır, her sorguda ayrı bir
                // sp_set_session_context turu olmaz. Bağlam scope sonunda kapanır; havuz oturum değerini temizler.
                await db.Database.OpenConnectionAsync(context.RequestAborted);

                // Destek oturumunda her API isteği, işlenmeden önce okuma günlüğüne yazılır (ADR-0028, Y1.3); yazılamazsa istek durur.
                if (membership.IsSupport && context.Request.Path.StartsWithSegments("/api"))
                {
                    await context.RequestServices.GetRequiredService<ISupportAccess>()
                        .RecordReadAsync(membership.MembershipId, context.Request.Method, context.Request.Path.Value ?? string.Empty, context.RequestAborted);
                }

                // ITenantSettings tenant DbContext'ine bağlıdır (ADR-0020); yalnızca aktif tenant kurulduktan SONRA çözülür.
                var settings = context.RequestServices.GetRequiredService<ITenantSettings>();

                // Firma iki adımlı doğrulamayı zorunlu kılıyorsa kapalı olan kişi önce kurulumu yapar (Y7b). Firma ayarı kısa süre
                // önbellekte tutulur (ADR-0048); kişinin kendi durumu yalnızca ayar açıkken ve her istekte sorulur.
                var requireTwoFactor = await cache.GetOrLoadAsync("require-two-factor", membership.TenantId, Guid.Empty, db,
                    () => settings.GetValueAsync(CoreModule.SettingKeys.RequireTwoFactor, context.RequestAborted));
                if (requireTwoFactor == CoreModule.SettingValues.Yes
                    && !await db.Users.AnyAsync(u => u.Id == userId && u.TwoFactorEnabled, context.RequestAborted))
                {
                    accessor.Current = null;
                    accessor.TwoFactorRequiredTenantId = membership.TenantId;

                    // Bağlam yok: tenant'ı oturuma yazılmış bağlantı bırakılır; sonraki sorgular tenant'sız yeni bağlantı açar.
                    await db.Database.CloseConnectionAsync();
                }
            }
        }

        await next(context);
    }
}

/// <summary>İstek başına aktif şirket bağlamı; <see cref="CompanyContextMiddleware"/> doldurur (ADR-0008, adım 2).</summary>
public sealed class HttpCompanyContextAccessor : ICompanyContextAccessor
{
    public CompanyContext? Current { get; set; }
}

/// <summary>
/// Aktif tenant kurulduktan sonra aktif şirketi oturumdan belirler ve her istekte erişilebilir şirketlere karşı doğrular
/// (ADR-0008, adım 2). Oturumdaki şirket claim'i yalnızca kişinin açık seçimidir; erişilemeyen veya eksik bir değer
/// varsayılan şirkete düşer. İstemcinin gönderdiği bir başlık veya parametre aktif şirketi belirlemez.
/// <see cref="TenantContextMiddleware"/>'den sonra çalışır.
/// </summary>
public sealed class CompanyContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, HttpTenantContextAccessor tenant, HttpCompanyContextAccessor company, IMembershipDirectory directory)
    {
        if (tenant.Current is { } current)
        {
            var access = await directory.GetAccessibleCompaniesAsync(current.MembershipId, context.RequestAborted);
            var accessibleIds = access.Companies.Select(c => c.Id).ToHashSet();

            // Oturumdaki açık seçim erişilebilirse o; değilse varsayılan şirket; o da yoksa ilk erişilebilir; hiç erişim yoksa null.
            Guid? active = Guid.TryParse(context.User.FindFirst(ModulixClaimTypes.CompanyId)?.Value, out var claimed) && accessibleIds.Contains(claimed)
                ? claimed
                : access.Companies.FirstOrDefault(c => c.IsDefault)?.Id ?? access.Companies.FirstOrDefault()?.Id;

            company.Current = new CompanyContext(active, accessibleIds, access.IsAllCompanies);
        }

        await next(context);
    }
}

/// <summary>
/// Aktif ve doğrulanmış tenant var mı; yoksa 403 yanıtı. Host içi süzgeç (<see cref="RequireTenantFilter"/>) ve modül uç
/// noktalarının süzgeçleri (<c>Modulix.Core.Hosting</c>, ADR-0046) aynı kuralı buradan okur.
/// </summary>
public sealed class HttpActiveTenantGuard(HttpTenantContextAccessor tenant) : IActiveTenantGuard
{
    public IResult? Check() =>
        tenant.Current is not null
            ? null
            : tenant.TwoFactorRequiredTenantId is not null
                ? LocalizedProblems.Problem(
                    StatusCodes.Status403Forbidden,
                    ServerMessages.Access.TwoFactorRequiredTitle,
                    ServerMessages.Access.TwoFactorRequiredDetail,
                    new Dictionary<string, object?> { ["code"] = TenantProblems.TwoFactorRequired })
                : LocalizedProblems.Problem(
                    StatusCodes.Status403Forbidden, ServerMessages.Record.NoActiveTenant, ServerMessages.Access.NoActiveTenantDetail);
}

/// <summary>Aktif ve doğrulanmış tenant yoksa isteği 403 ile durdurur.</summary>
public sealed class RequireTenantFilter(IActiveTenantGuard guard) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        guard.Check() is { } problem ? ValueTask.FromResult<object?>(problem) : next(context);
}

public static class TenancySetup
{
    public static IServiceCollection AddModulixTenancy(this IServiceCollection services)
    {
        services.AddScoped<HttpTenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(sp => sp.GetRequiredService<HttpTenantContextAccessor>());
        services.AddScoped<IActiveTenantGuard, HttpActiveTenantGuard>();
        services.AddScoped<HttpCompanyContextAccessor>();
        services.AddScoped<ICompanyContextAccessor>(sp => sp.GetRequiredService<HttpCompanyContextAccessor>());
        services.AddSingleton<ITenantScopeFactory, TenantScopeFactory>();
        services.AddScoped<Modulix.Core.Contracts.Modules.IGrantedExecution, Modulix.Api.Jobs.GrantedExecution>();
        return services;
    }

    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tenant/current", async (ITenantContextAccessor tenant, IMembershipDirectory directory, CancellationToken ct) =>
            {
                var current = tenant.Current!;
                var membership = await directory.FindActiveAsync(current.UserId, current.TenantId, ct);
                return membership is null
                    ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Aktif tenant yok.")
                    : Results.Ok(new CurrentTenantResponse(membership.TenantId, membership.TenantName, membership.TenantSlug));
            })
            .RequireAuthorization()
            .AddEndpointFilter<RequireTenantFilter>()
            .WithTags("Tenant");

        // Sorumlu seçimi için üye adları; her üye görebilir (e-posta ve rol bilgisi verilmez). assignable=true: yalnızca
        // kişinin kayıt kapsamındaki üyeler (Y8), yani sorumlu olarak atayabileceği ve süzgeçte seçebileceği kişiler.
        // Kapsam kayıt türüne göre çözüldüğü için assignable=true ile entityKey (ör. crm.opportunity) zorunludur (ADR-0030).
        app.MapGet("/api/tenant/members", async (
                bool? assignable, string? entityKey, ITenantMembers members, IDataScopeService scope, ITenantContextAccessor tenant,
                CancellationToken ct) =>
            {
                if (assignable == true && string.IsNullOrWhiteSpace(entityKey))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["entityKey"] = ["Atanabilir üyeler için kayıt türü anahtarı gerekir."],
                    });
                }

                var owners = assignable == true ? await scope.GetOwnersAsync(tenant.Current!, entityKey!, ct) : null;
                return Results.Ok((await members.ListActiveAsync(ct))
                    .Where(m => owners is null || owners.Contains(m.UserId))
                    .Select(m => new TenantMemberResponse(m.UserId, m.DisplayName))
                    .ToList());
            })
            .RequireAuthorization()
            .AddEndpointFilter<RequireTenantFilter>()
            .WithTags("Tenant");

        return app;
    }
}
