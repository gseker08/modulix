using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Hosting;
using Modulix.Modules.Crm.Leads;
using Modulix.Shared.Crm;

namespace Modulix.Modules.Crm.Endpoints;

/// <summary>
/// Herkese açık Lead toplama ucu: <c>POST /api/public/lead-forms/{tenant}/{formKey}</c> ve CORS ön kontrolü (<c>OPTIONS</c>).
/// <list type="bullet">
///   <item><b>Tenant</b> URL'deki firma kısa adından (ya da kimliğinden, N biçimi) sunucuda <see cref="IPublicTenantScopes"/> ile çözülür; gövdede/başlıkta tenant kimliği aranmaz. İstek, oturumdan ve
///   çerezden bağımsız, "crm.lead-intake" sistem kimliğiyle (dar izinler) kendi tenant kapsamında çalışır.</item>
///   <item><b>Form kimliği gizli değildir</b> (sayfada görünür); tek başına yalnızca izinli kaynaktan ve yalnızca izinli alanlarla Lead
///   oluşturma imkânı verir. Sunucudan sunucuya çağrılar <c>X-Lead-Form-Secret</c> başlığını kullanır; sır tarayıcıya konmaz.</item>
///   <item><b>Origin/CORS</b> yalnızca tarayıcıların başka siteden gönderimini sınırlar; kimlik doğrulama değildir.</item>
///   <item>Hız sınırı (IP + form başına), 32 KB gövde sınırı, alan/uzunluk doğrulaması, bal tuzağı ve tekrar engeli uygulanır. Yanıt, kişinin
///   ya da e-postanın sistemde olup olmadığını belli etmez.</item>
/// </list>
/// </summary>
internal static class LeadIntakeEndpoints
{
    public const string RateLimitPolicy = "lead-intake";
    public const string SecretHeader = "X-Lead-Form-Secret";
    public const int MaxBodyBytes = 32 * 1024;

    /// <summary>Formun sistem kimliği: izinler yalnızca "crm.lead-intake" aboneliğinin bildirdikleridir; kayıt boru hattı bunu uygular.</summary>
    private const string Identity = "crm.lead-intake";

    /// <summary>
    /// IP + form başına dakikadaki gönderim sınırı (<c>LeadIntake:RateLimitPerMinute</c>, en az 1, varsayılan 20). Modülün <c>ConfigureServices</c>'inde
    /// kaydolur; sınır istek anında okunur.
    /// </summary>
    public static void AddLeadIntakeRateLimit(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicy, context =>
            {
                var permits = RateLimits.PerNode(context.RequestServices.GetRequiredService<IConfiguration>(), "LeadIntake:RateLimitPerMinute", 20);
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{context.Connection.RemoteIpAddress}|{context.Request.RouteValues["tenant"]}|{context.Request.RouteValues["formKey"]}",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            });
        });

    /// <summary><paramref name="routes"/> host tarafından oturumsuz <c>/api/public</c> grubu olarak gelir (ADR-0046); yol <c>/api/public/lead-forms/{tenant}/{formKey}</c>.</summary>
    public static void MapPublic(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/lead-forms/{tenant}/{formKey}")
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags("Müşteri adayı formu (herkese açık)");
        group.MapPost("", SubmitAsync);
        group.MapMethods("", ["OPTIONS"], PreflightAsync);
    }

    /// <summary>Form yönetimi (oturumlu, <c>/api/crm</c> grubunda): izin crm.lead-forms.manage.</summary>
    public static void MapAdmin(IEndpointRouteBuilder crm)
    {
        ResourceEndpoints.MapResource<LeadFormDto, SaveLeadFormRequest>(crm, "/api/crm", "lead-forms", CrmModule.Entities.LeadForm,
            CrmModule.Permissions.LeadFormsManage, CrmModule.Permissions.LeadFormsManage);
        crm.MapPost("/lead-forms/{id:guid}/secret", RotateSecretAsync).RequirePermission(CrmModule.Permissions.LeadFormsManage);
    }

    private static async Task<IResult> RotateSecretAsync(Guid id, LeadVersionRequest request, ILeadFormAdmin admin, CancellationToken ct)
    {
        var result = await admin.RotateSecretAsync(id, request.Version, ct);
        return result.IsSuccess
            ? Results.Ok(new LeadFormSecretDto(ResourceEndpoints.ToDto<LeadFormDto>(result.Value!.Form), result.Value.Secret))
            : RecordProblems.ToProblem(result.Error!);
    }

    private static async Task<IResult> SubmitAsync(
        string tenant, string formKey, HttpContext http, IPublicTenantScopes scopes, CancellationToken ct)
    {
        // Gövde sınırı (Kestrel); salt okunur olduğunda (test sunucusu) uygulanamaz.
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
        {
            sizeLimit.MaxRequestBodySize = MaxBodyBytes;
        }

        if (http.Request.ContentLength > MaxBodyBytes)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, CrmMessages.Endpoint.BodyTooLarge);
        }


        if (!http.Request.HasJsonContentType())
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, CrmMessages.Endpoint.JsonRequired);
        }

        JsonObject? fields;
        try
        {
            fields = await http.Request.ReadFromJsonAsync<JsonObject>(JsonSerializerOptions.Web, ct);
        }
        catch (Exception exception) when (exception is JsonException or BadHttpRequestException or InvalidOperationException)
        {
            return Problem(StatusCodes.Status400BadRequest, CrmMessages.Endpoint.InvalidBody);
        }

        if (fields is null)
        {
            return Problem(StatusCodes.Status400BadRequest, CrmMessages.Endpoint.InvalidBody);
        }

        // Bilinmeyen ya da erişime kapalı firma ve kapalı ya da salt okunur modül (ADR-0028) aynı yanıtı alır: form kabul edilmez.
        if (await scopes.OpenAsync(tenant, Identity, write: true, http.TraceIdentifier, ct) is not { } opened)
        {
            return Problem(StatusCodes.Status404NotFound, CrmMessages.Endpoint.FormNotFound);
        }

        await using var scope = opened;
        var intake = scope.ServiceProvider.GetRequiredService<ILeadIntake>();

        var origin = http.Request.Headers.Origin.ToString();
        var secret = http.Request.Headers[SecretHeader].ToString();
        var result = await intake.SubmitAsync(new LeadIntakeSubmission(formKey, origin, secret, fields), ct);

        if (await intake.AllowedOriginAsync(formKey, origin, ct) is { Length: > 0 } allowed)
        {
            AddCors(http, allowed);
        }

        return result.Outcome switch
        {
            LeadIntakeOutcome.Accepted => Results.Json(new PublicLeadFormResponse(true, null), statusCode: StatusCodes.Status202Accepted),
            LeadIntakeOutcome.Invalid => Results.ValidationProblem(
                result.Errors!.ToDictionary(e => e.Key, e => e.Value), title: "Form gönderilemedi.", statusCode: StatusCodes.Status400BadRequest),
            LeadIntakeOutcome.NotFound => Problem(StatusCodes.Status404NotFound, CrmMessages.Endpoint.FormNotFound),
            _ => Problem(StatusCodes.Status403Forbidden, CrmMessages.Endpoint.SubmissionRejected),
        };
    }

    private static async Task<IResult> PreflightAsync(
        string tenant, string formKey, HttpContext http, IPublicTenantScopes scopes, CancellationToken ct)
    {
        // Ön kontrol yazmaz; modül kapalıysa kapsam açılmaz ve CORS başlığı verilmez (gönderim de kabul edilmez).
        var origin = http.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && await scopes.OpenAsync(tenant, Identity, write: false, http.TraceIdentifier, ct) is { } opened)
        {
            await using var scope = opened;
            if (await scope.ServiceProvider.GetRequiredService<ILeadIntake>().AllowedOriginAsync(formKey, origin, ct) is { Length: > 0 } allowed)
            {
                AddCors(http, allowed);
                http.Response.Headers.AccessControlAllowMethods = "POST, OPTIONS";
                http.Response.Headers.AccessControlAllowHeaders = "Content-Type";
                http.Response.Headers.AccessControlMaxAge = "600";
            }
        }

        return Results.NoContent();
    }

    private static void AddCors(HttpContext http, string origin)
    {
        http.Response.Headers.AccessControlAllowOrigin = origin;
        http.Response.Headers.Vary = "Origin";
    }

    private static IResult Problem(int status, ServerMessage title) => LocalizedProblems.Problem(status, title);
}
