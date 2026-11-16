using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulix.Core.Contracts.Authorization;
using Modulix.Core.Contracts.Events;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Hosting;
using Modulix.Modules.Contracts.Lifecycle;
using Modulix.Shared.Contracts;

namespace Modulix.Modules.Contracts.Endpoints;

/// <summary>
/// İmza uç noktaları (ADR-0022, dilim 6).
/// <list type="bullet">
///   <item><b>İç uçlar</b> (<c>/api/contracts/contracts/{id}/signature...</c>): oturumlu; izinler serviste denetlenir (kayıt bulunduktan sonra: kapsam dışı 404, izinsiz 403).
///   Manuel imza kanıtı JSON içinde base64 gelir (PDF/PNG/JPEG, en fazla 10 MB; dosya imzası denetlenir) ve her zaman indirme olarak sunulur.</item>
///   <item><b>Sağlayıcı webhook'u</b> (<c>POST /api/public/contracts/signature/{provider}/events</c>): oturumsuz; kimlik doğrulama HMAC-SHA256 imzasıdır
///   (<c>X-Modulix-Timestamp</c>, <c>X-Modulix-Signature</c>; zaman damgası 5 dakika içinde olmalı, tekrar saldırısını sınırlar). Gizli anahtar yapılandırmadan gelir
///   (<c>ContractSignature:{Sağlayıcı}:WebhookSecret</c>); yoksa uç nokta o sağlayıcı için kapalıdır (404). Tenant, isteğin sağlayıcı referansından çözülür ve istek
///   "contracts.signature-webhook" sistem kimliğiyle kendi tenant kapsamında çalışır. Olaylar idempotenttir (aynı olay kimliği bir kez) ve sıra denetimlidir.
///   Hız sınırı ve 16 KB gövde sınırı uygulanır; hata yanıtları tek tiptir.</item>
/// </list>
/// Modulix müşteriye herkese açık bir imza sayfası ya da bağlantısı sunmaz: dış imzacı ya imzalı belgeyi manuel yollar ya da sağlayıcının kendi sayfasında imzalar.
/// </summary>
internal static class ContractSignatureEndpoints
{
    public const string WebhookPolicy = "contract-signature-webhook";
    public const int MaxWebhookBytes = 16 * 1024;
    private const string WebhookIdentity = "contracts.signature-webhook";
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    /// <summary>Webhook'un hız sınırı (IP + sağlayıcı başına dakikada <c>ContractSignature:RateLimitPerMinute</c>, en az 2, varsayılan 120); modülde kaydolur, sınır istek anında okunur.</summary>
    public static void AddContractSignatureRateLimit(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(WebhookPolicy, context =>
            {
                var permits = Math.Max(2, context.RequestServices.GetRequiredService<IConfiguration>().GetValue("ContractSignature:RateLimitPerMinute", 120));
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"signature-webhook|{context.Connection.RemoteIpAddress}|{context.Request.RouteValues["provider"]}",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            });
        });

    public static void MapInternal(RouteGroupBuilder contracts)
    {
        contracts.MapGet("/{id:guid}/signature", async (Guid id, IContractSignatures signatures, CancellationToken ct) =>
            Overview(await signatures.GetAsync(id, ct))).RequirePermission(ContractsModule.Permissions.ContractsRead);

        contracts.MapPost("/{id:guid}/signature", async (Guid id, StartSignatureRequest r, IContractSignatures signatures, CancellationToken ct) =>
            Overview(await signatures.StartAsync(id, r.Version, new StartSignatureInput(
                r.DocumentId, r.Method, r.Mode, r.ValidityDays,
                [.. (r.Signatories ?? []).Select(s => new SignatoryInput(s.Kind, s.UserId, s.Name, s.Email, s.Capacity, s.IsRequired))]), ct), created: true))
            .RequirePermission(ContractsModule.Permissions.ContractsRead);

        contracts.MapPost("/{id:guid}/signature/signatories/{signatoryId:guid}/manual", async (Guid id, Guid signatoryId, RecordManualSignatureRequest r, IContractSignatures signatures, CancellationToken ct) =>
        {
            byte[] content;
            try
            {
                content = Convert.FromBase64String(r.ContentBase64 ?? string.Empty);
            }
            catch (FormatException)
            {
                return RecordProblems.ToProblem(new RecordError(RecordErrorCode.ValidationFailed, "Dosya içeriği geçersiz.", [new RecordFieldError("file", "Dosya içeriği geçersiz (base64).")]));
            }

            return Overview(await signatures.RecordManualAsync(id, signatoryId, new ManualSignatureInput(r.SignedOn, r.Note, r.FileName ?? string.Empty, content), ct));
        }).RequirePermission(ContractsModule.Permissions.ContractsRead);

        contracts.MapPost("/{id:guid}/signature/signatories/{signatoryId:guid}/decline", async (Guid id, Guid signatoryId, SignatureReasonRequest r, IContractSignatures signatures, CancellationToken ct) =>
            Overview(await signatures.DeclineAsync(id, signatoryId, r.Reason ?? string.Empty, ct))).RequirePermission(ContractsModule.Permissions.ContractsRead);

        contracts.MapPost("/{id:guid}/signature/signatories/{signatoryId:guid}/remind", async (Guid id, Guid signatoryId, IContractSignatures signatures, CancellationToken ct) =>
            Overview(await signatures.RemindAsync(id, signatoryId, ct))).RequirePermission(ContractsModule.Permissions.ContractsRead);

        contracts.MapPost("/{id:guid}/signature/cancel", async (Guid id, SignatureReasonRequest r, IContractSignatures signatures, CancellationToken ct) =>
            Overview(await signatures.CancelAsync(id, r.Reason ?? string.Empty, ct))).RequirePermission(ContractsModule.Permissions.ContractsRead);

        contracts.MapPost("/{id:guid}/signature/expire", async (Guid id, IContractSignatures signatures, CancellationToken ct) =>
            Overview(await signatures.ExpireAsync(id, ct))).RequirePermission(ContractsModule.Permissions.ContractsRead);

        contracts.MapPost("/{id:guid}/signature/simulate", async (Guid id, SimulateSignatureRequest r, IContractSignatures signatures, CancellationToken ct) =>
            Overview(await signatures.SimulateAsync(id, r.SignatoryPosition, r.Type ?? string.Empty, r.Note, ct))).RequirePermission(ContractsModule.Permissions.ContractsRead);

        contracts.MapGet("/{id:guid}/signature/files/{fileId:guid}", async (Guid id, Guid fileId, IContractSignatures signatures, HttpContext http, CancellationToken ct) =>
        {
            var result = await signatures.GetEvidenceAsync(id, fileId, ct);
            if (!result.IsSuccess)
            {
                return RecordProblems.ToProblem(result.Error!);
            }

            // Yüklenen dosya sunucuda üretilmedi ve taranmadı: tarayıcıda çizilmez, her zaman indirme olarak ve içerik türü koklanmadan sunulur.
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
            return Results.File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
        }).RequirePermission(ContractsModule.Permissions.ContractsRead);
    }

    /// <summary><paramref name="routes"/> host tarafından oturumsuz <c>/api/public</c> grubu olarak gelir (ADR-0046); yol <c>/api/public/contracts/signature/{provider}/events</c>.</summary>
    public static void MapPublic(IEndpointRouteBuilder routes) =>
        routes.MapPost("/contracts/signature/{provider}/events", WebhookAsync)
            .RequireRateLimiting(WebhookPolicy).WithTags("Sözleşme imza sağlayıcısı webhook (herkese açık, imzalı)");

    private static async Task<IResult> WebhookAsync(
        string provider, HttpContext http, IConfiguration configuration, IPublicTenantScopes scopes, TimeProvider time, ILogger<IContractSignatures> log, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxWebhookBytes;
        }

        if (http.Request.ContentLength > MaxWebhookBytes)
        {
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Gövde çok büyük.");
        }

        // Yalnızca yapılandırması (gizli anahtarı) olan sağlayıcı için açıktır; bilinmeyen sağlayıcı ile yapılandırılmamış sağlayıcı aynı yanıtı alır.
        var name = provider.Trim().ToLowerInvariant();
        var secret = configuration[$"ContractSignature:{ConfigName(name)}:WebhookSecret"];
        if (name != SimulationSignatureProvider.ProviderName || string.IsNullOrWhiteSpace(secret))
        {
            return Results.NotFound();
        }

        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();
        if (body.Length > MaxWebhookBytes)
        {
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Gövde çok büyük.");
        }

        var (valid, failure) = SignatureWebhook.Verify(secret, http.Request.Headers["X-Modulix-Timestamp"].ToString(), http.Request.Headers["X-Modulix-Signature"].ToString(), body, time.GetUtcNow());
        if (!valid)
        {
            log.LogWarning("Sözleşme imza webhook'u reddedildi ({Provider}): {Reason}", name, failure);
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "İmza geçersiz.");
        }

        WebhookBody? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookBody>(body, Json);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Reference) || !TryTenantOf(payload.Reference, out var tenantId))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Olay geçersiz.");
        }

        // Erişime açık olmayan firma ve yazılabilir olmayan (kapalı ya da salt okunur) modül aynı yanıtı alır; istek "contracts.signature-webhook" kimliğiyle çalışır.
        if (await scopes.OpenAsync(tenantId.ToString("N"), WebhookIdentity, write: true, http.TraceIdentifier, ct) is not { } opened)
        {
            return Results.NotFound();
        }

        await using var scope = opened;

        var result = await scope.ServiceProvider.GetRequiredService<IContractSignatures>().ApplyProviderEventAsync(
            new ProviderEventInput(name, payload.EventId ?? string.Empty, payload.Sequence, payload.Type ?? string.Empty, payload.Reference, payload.SignatoryPosition, payload.OccurredAt ?? time.GetUtcNow(), payload.Assurance, payload.Note), ct);
        if (!result.IsSuccess)
        {
            return result.Error!.Code switch
            {
                RecordErrorCode.NotFound => Results.NotFound(),
                RecordErrorCode.ValidationFailed => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Olay geçersiz."),
                // Sürüm çakışması ve geçici hatalar: sağlayıcı yeniden dener (olay idempotenttir).
                _ => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Olay şu an uygulanamadı; yeniden deneyin."),
            };
        }

        return Results.Ok(new SignatureWebhookResult(result.Value!.Outcome));
    }

    /// <summary>Sağlayıcı referansı <c>{tenant:N}.{rastgele}</c> biçimindedir: ilk parça tenant kimliğidir (gizli değil; kimlik doğrulama imzadadır).</summary>
    internal static bool TryTenantOf(string reference, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        var dot = reference.IndexOf('.');
        return dot == 32 && Guid.TryParseExact(reference[..dot], "N", out tenantId);
    }

    private static string ConfigName(string provider) => provider.Length == 0 ? provider : char.ToUpperInvariant(provider[0]) + provider[1..];

    private sealed record WebhookBody(string? EventId, int Sequence, string? Type, string? Reference, int? SignatoryPosition, DateTimeOffset? OccurredAt, string? Assurance, string? Note);

    private static IResult Overview(RecordResult<ContractSignatureOverview> result, bool created = false)
    {
        if (!result.IsSuccess)
        {
            return RecordProblems.ToProblem(result.Error!);
        }

        var dto = JsonSerializer.Deserialize<ContractSignatureDto>(JsonSerializer.Serialize(result.Value!, Json), Json)!;
        return created ? Results.Json(dto, Json, statusCode: StatusCodes.Status201Created) : Results.Ok(dto);
    }
}
