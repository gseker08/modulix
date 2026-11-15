using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Localization;
using Modulix.Core.Modules;
using Modulix.Core.Settings;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Api.Localization;

/// <summary>
/// Sunucu dil çözümü (ADR-0034 §6, dilim 18a): kullanıcı oturumdan (isteğin kimliği, yoksa arka plan bağlamının kullanıcısı),
/// tenant aktif tenant bağlamından belirlenir; istemcinin gönderdiği başlık (<c>Accept-Language</c>) ya da parametre dili belirlemez.
/// </summary>
public static class ServerLocalizationSetup
{
    public static IServiceCollection AddModulixServerLocalization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ILanguagePreferences, SessionLanguagePreferences>();
        services.AddScoped<IRequestLanguage, RequestLanguage>();
        services.AddScoped<ILocalizer, Localizer>();
        return services;
    }
}

/// <summary>
/// <see cref="ILanguagePreferences"/>: kullanıcının tercihi <c>platform</c> kullanıcı tablosundan (RLS dışı), tenant varsayılanı
/// firma ayarından (<see cref="CoreModule.SettingKeys.Language"/>) okunur. Firma ayarı yalnızca aktif tenant varken çözülür
/// (tenant bağlamına bağlıdır); başka tenant'ın ayarı hiçbir yoldan okunmaz.
/// </summary>
public sealed class SessionLanguagePreferences(
    IHttpContextAccessor http, ITenantContextAccessor tenant, ModulixDbContext db, IServiceProvider services) : ILanguagePreferences
{
    public async Task<string?> GetUserPreferenceAsync(CancellationToken cancellationToken)
    {
        Guid? userId = Guid.TryParse(http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var fromSession)
            ? fromSession
            : tenant.Current?.UserId is { } fromContext && fromContext != Guid.Empty ? fromContext : null;
        if (userId is not { } id)
        {
            return null;
        }

        return await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.PreferredLanguage).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetTenantDefaultAsync(CancellationToken cancellationToken) =>
        tenant.Current is null
            ? null
            : await services.GetRequiredService<ITenantSettings>().GetValueAsync(CoreModule.SettingKeys.Language, cancellationToken);
}

/// <summary>
/// Yanıtı yazılırken isteğin diline çevrilen sonuç: <paramref name="build"/> çeviri işleviyle asıl sonucu (problem ayrıntısı) kurar.
/// Uç nokta süzgeçleri ve <c>ToProblem</c> gibi durumsuz yardımcılar sonucu eşzamanlı döndürür; dil yalnızca yanıt yazılırken
/// (gerekirse) çözülür.
/// </summary>
public sealed class LocalizedResult(Func<Func<ServerMessage, string>, IResult> build) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var services = httpContext.RequestServices;
        var language = await services.GetRequiredService<IRequestLanguage>().GetAsync(httpContext.RequestAborted);
        var catalog = services.GetRequiredService<ITextCatalog>();
        await build(message => catalog.Translate(language, message)).ExecuteAsync(httpContext);
    }
}

/// <summary>Çevrilen problem ayrıntıları.</summary>
public static class LocalizedProblems
{
    /// <summary><see cref="Results.Problem(string?, string?, int?, string?, string?, IDictionary{string, object?}?)"/>, başlık ve ayrıntı isteğin dilinde.</summary>
    public static IResult Problem(int statusCode, ServerMessage title, ServerMessage? detail = null, IDictionary<string, object?>? extensions = null) =>
        new LocalizedResult(t => Results.Problem(statusCode: statusCode, title: t(title), detail: detail is null ? null : t(detail), extensions: extensions));
}
