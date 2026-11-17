using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Hosting;
using Modulix.Modules.Support.Lifecycle;
using Modulix.Shared.Support;

namespace Modulix.Modules.Support.Endpoints;

/// <summary>
/// Dilim 4 uç noktaları (ADR-0024): ekler, gelen kanal girişi, memnuniyet anketi, CSV dışa aktarma ve toplu işlem.
/// <list type="bullet">
///   <item>Ekler mesaja bağlıdır; iç nota bağlı ek iç not iznine bağlıdır. İndirme her zaman <c>attachment</c> olarak ve <c>nosniff</c> ile verilir (tarayıcı içerikten çalıştırmaz).</item>
///   <item>Gelen kanal girişi (<c>POST /api/support/inbound</c>) bir adaptör sözleşmesidir; çağıran <c>support.inbound.ingest</c> + talep açma + yanıt izinli bir servis kullanıcısıdır.</item>
///   <item>Anket sayfası herkese açıktır (<c>/api/public/support-survey/{tenant}</c>): kimlik doğrulama bağlantı belirtecine sahip olmaktır, belirteç gövdede taşınır, hız sınırı ve tek tip hata uygulanır.</item>
/// </list>
/// </summary>
internal static class SupportChannelEndpoints
{
    public const string SurveyPolicy = "support-survey";

    /// <summary>Anketin sistem kimliği: yalnızca talebi okur, tarihçe ve son hareket yazar (<see cref="SupportSurveyAnchor"/>).</summary>
    private const string SurveyIdentity = "support.survey";
    private const int ExportMaxRows = 5000;
    private const int SurveyBodyMaxBytes = 4 * 1024;
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    /// <summary>
    /// Herkese açık anketin hız sınırı politikası (IP + tenant başına dakikada <c>SupportSurveyPublic:RateLimitPerMinute</c>, en az 2, varsayılan 30). Modülün
    /// <c>ConfigureServices</c>'inde kaydolur; sınır yapılandırmadan istek anında okunur.
    /// </summary>
    public static IServiceCollection AddSupportSurveyRateLimit(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(SurveyPolicy, context =>
            {
                var permits = RateLimits.PerNode(context.RequestServices.GetRequiredService<IConfiguration>(), "SupportSurveyPublic:RateLimitPerMinute", 30, minimum: 2);
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"survey|{context.Connection.RemoteIpAddress}|{context.Request.RouteValues["tenant"]}",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            });
        });

    /// <summary>Oturumlu uçlar: <c>group</c> /api/support, <c>tickets</c> /api/support/tickets.</summary>
    public static void MapInternal(RouteGroupBuilder group, RouteGroupBuilder tickets)
    {
        // Gelen kanal girişi. Ek izinler (talep oluşturma, yanıt) serviste denetlenir; kapsam çağıranın kapsamıdır.
        group.MapPost("/inbound", async (InboundMessageRequest r, ITicketInbound inbound, CancellationToken ct) =>
        {
            var result = await inbound.IngestAsync(new InboundMessage(
                r.Channel, r.ExternalMessageId, r.InReplyTo, r.References, r.FromAddress, r.FromName, r.Subject, r.Body,
                r.AutoSubmitted, r.Precedence, r.SuppressAutoResponse, r.NullReturnPath), ct);
            if (!result.IsSuccess)
            {
                return RecordProblems.ToProblem(result.Error!);
            }

            var dto = new InboundResultDto(result.Value!.Outcome, result.Value.TicketId, result.Value.MessageId, result.Value.TicketNumber);
            return result.Value.Outcome switch
            {
                "Throttled" => Results.Json(dto, statusCode: StatusCodes.Status429TooManyRequests),
                InboundOutcome.Created => Results.Created($"{Prefix}/tickets/{dto.TicketId}", dto),
                _ => Results.Ok(dto),
            };
        }).RequirePermission(SupportModule.Permissions.InboundIngest);

        // Ekler.
        tickets.MapGet("/{id:guid}/attachments", async (Guid id, ITicketAttachments attachments, CancellationToken ct) =>
        {
            var result = await attachments.ListAsync(id, ct);
            return result.IsSuccess ? Results.Ok(Map<List<AttachmentDto>>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);

        // Gövde ham dosyadır (JSON/base64 şişmesi yok); ad sorgudadır. Sınır sunucuda uygulanır, istemci başlığına güvenilmez.
        tickets.MapPost("/{id:guid}/messages/{messageId:guid}/attachments", async (Guid id, Guid messageId, string? fileName, HttpRequest http, ITicketAttachments attachments, CancellationToken ct) =>
        {
            var limit = AttachmentRules.MaxBytes;
            if (http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
            {
                sizeLimit.MaxRequestBodySize = limit + 1024;
            }

            if (http.ContentLength > limit)
            {
                return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: $"Dosya en fazla {limit / (1024 * 1024)} MB olabilir.");
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await http.Body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > limit)
                {
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: $"Dosya en fazla {limit / (1024 * 1024)} MB olabilir.");
                }

                buffer.Write(chunk, 0, read);
            }

            var result = await attachments.AddAsync(id, messageId, fileName, buffer.ToArray(), ct);
            return result.IsSuccess ? Results.Created($"{Prefix}/tickets/{id}/attachments", Map<AttachmentDto>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);

        tickets.MapGet("/{id:guid}/attachments/{attachmentId:guid}/file", async (Guid id, Guid attachmentId, ITicketAttachments attachments, HttpContext http, CancellationToken ct) =>
        {
            var result = await attachments.GetAsync(id, attachmentId, ct);
            if (!result.IsSuccess)
            {
                return RecordProblems.ToProblem(result.Error!);
            }

            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            return Results.File(result.Value!.Content, result.Value.Info.ContentType, result.Value.Info.FileName);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);

        // İlişkili kayıtlar (sözleşme, sipariş, teslimat): bağlama çağıranın yetkisiyle doğrulanır; talep düzenleme izni gerekir.
        tickets.MapGet("/{id:guid}/links", async (Guid id, ITicketLinks links, CancellationToken ct) =>
        {
            var result = await links.ListAsync(id, ct);
            return result.IsSuccess ? Results.Ok(Map<List<TicketLinkDto>>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);
        tickets.MapPost("/{id:guid}/links", async (Guid id, AddTicketLinkRequest r, ITicketLinks links, CancellationToken ct) =>
        {
            var result = await links.AddAsync(id, r.EntityKey, r.RecordId, ct);
            return result.IsSuccess ? Results.Created($"{Prefix}/tickets/{id}/links", Map<List<TicketLinkDto>>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);
        tickets.MapDelete("/{id:guid}/links/{linkId:guid}", async (Guid id, Guid linkId, ITicketLinks links, CancellationToken ct) =>
        {
            var result = await links.RemoveAsync(id, linkId, ct);
            return result.IsSuccess ? Results.Ok(Map<List<TicketLinkDto>>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);

        // Memnuniyet anketleri (personel görünümü; belirteç dönmez).
        tickets.MapGet("/{id:guid}/surveys", async (Guid id, ITicketSurveys surveys, CancellationToken ct) =>
        {
            var result = await surveys.ListAsync(id, ct);
            return result.IsSuccess ? Results.Ok(Map<List<SurveyDto>>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);

        // Toplu işlem: her talep kendi eyleminin izin, kapsam ve kuralıyla çalışır; sonuçlar talep başına döner (atomik değildir).
        tickets.MapPost("/bulk", async (BulkTicketsRequest r, ITicketBulk bulk, CancellationToken ct) =>
        {
            var result = await bulk.RunAsync(r.Action, r.Ids ?? [], new BulkInput(r.Reason, r.OwnerId, r.QueueId), ct);
            return result.IsSuccess ? Results.Ok(Map<List<BulkTicketResultDto>>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);

        tickets.MapGet("/export", ExportAsync).RequirePermission(SupportModule.Permissions.TicketsExport);

        // Düzeltici aksiyonlar (şikâyet inceleme): okuma talep okuma iziniyle, yazma support.complaints.manage ile (serviste, talep bulunduktan SONRA).
        tickets.MapGet("/{id:guid}/corrective-actions", async (Guid id, ICorrectiveActions actions, CancellationToken ct) =>
        {
            var result = await actions.ListAsync(id, ct);
            return result.IsSuccess ? Results.Ok(Map<List<CorrectiveActionDto>>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);
        tickets.MapPost("/{id:guid}/corrective-actions", async (Guid id, AddCorrectiveActionRequest r, ICorrectiveActions actions, CancellationToken ct) =>
        {
            var result = await actions.AddAsync(id, r.Title, r.OwnerId, r.DueDate, ct);
            return result.IsSuccess ? Results.Created($"{Prefix}/tickets/{id}/corrective-actions", Map<List<CorrectiveActionDto>>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);
        tickets.MapPost("/{id:guid}/corrective-actions/{actionId:guid}/status", async (Guid id, Guid actionId, SetCorrectiveActionStatusRequest r, ICorrectiveActions actions, CancellationToken ct) =>
        {
            var result = await actions.SetStatusAsync(id, actionId, r.Status, r.Note, ct);
            return result.IsSuccess ? Results.Ok(Map<List<CorrectiveActionDto>>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.TicketsRead);

        // Raporlar: veri çağıranın izin ve veri kapsamıyla kayıt boru hattından okunur (liste ekranını aşmaz); her sayı bir kırılım süzgeci taşır.
        var reports = group.MapGroup("/reports");
        reports.MapGet("/overview", async (DateOnly? from, DateOnly? to, string? granularity, string? typeKey, Guid? queueId, Guid? categoryId, string? priority, Guid? partyId, ISupportReports service, CancellationToken ct) =>
        {
            var result = await service.OverviewAsync(new ReportQuery(from, to, granularity, typeKey, queueId, categoryId, priority, partyId), ct);
            return result.IsSuccess ? Results.Ok(Map<ReportOverviewDto>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.ReportsRead);
        reports.MapGet("/complaints", async (DateOnly? from, DateOnly? to, int? minOccurrences, string? typeKey, Guid? queueId, Guid? categoryId, string? priority, Guid? partyId, ISupportReports service, CancellationToken ct) =>
        {
            var result = await service.ComplaintsAsync(new ReportQuery(from, to, null, typeKey, queueId, categoryId, priority, partyId), minOccurrences, ct);
            return result.IsSuccess ? Results.Ok(Map<ComplaintReviewDto>(result.Value!)) : RecordProblems.ToProblem(result.Error!);
        }).RequirePermission(SupportModule.Permissions.ReportsRead);
    }

    private const string Prefix = "/api/support";

    /// <summary>
    /// CSV dışa aktarma. Kayıt boru hattından okunur (izin ve veri kapsamı liste ekranıyla aynı); mesaj gövdesi, iç not ve çözüm notu sütunu yoktur.
    /// Çıkış denetim kaydına yazılır (kim, kaç kayıt, hangi süzgeç). Hücre başındaki formül karakterleri etkisizleştirilir.
    /// </summary>
    private static async Task<IResult> ExportAsync(
        HttpRequest http, string? search, IRecordPipeline pipeline, IRecordExportAudit exportAudit, CancellationToken ct)
    {
        var filters = RecordRequests.FiltersFrom(http) ?? new Dictionary<string, string>();
        var items = new List<RecordData>();
        for (var page = 1; items.Count < ExportMaxRows; page++)
        {
            var result = await pipeline.QueryAsync(new RecordQuery(SupportModule.Entities.Ticket, page, RecordQuery.MaxPageSize, search, filters), ct);
            if (!result.IsSuccess)
            {
                return RecordProblems.ToProblem(result.Error!);
            }

            items.AddRange(result.Value!.Items);
            if (result.Value.Items.Count == 0 || items.Count >= result.Value.TotalCount)
            {
                break;
            }
        }

        await exportAudit.RecordAsync(SupportModule.Entities.Ticket, items.Count, filters, null, ct);

        string[] columns =
        [
            "number", "subject", "typeKey", "channel", "customerName", "reporterName", "reporterEmail", "status", "priority", "impact", "urgency", "categoryId", "queueId",
            "ownerId", "createdAt", "firstResponseAt", "firstResponseDueAt", "resolutionDueAt", "resolvedAt", "closedAt", "reopenCount",
        ];
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', columns));
        foreach (var item in items)
        {
            csv.AppendLine(string.Join(',', columns.Select(c => CsvText.Escape(item.Values[c]?.ToString()))));
        }

        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", "talepler.csv");
    }

    // ---- herkese açık anket uçları

    /// <summary><paramref name="routes"/> host tarafından oturumsuz <c>/api/public</c> grubu olarak gelir (ADR-0046); yollar <c>/api/public/support-survey/{tenant}/…</c>.</summary>
    public static void MapPublic(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/support-survey/{tenant}").WithTags("Destek memnuniyet anketi (herkese açık)");
        group.MapPost("/view", ViewAsync).RequireRateLimiting(SurveyPolicy);
        group.MapPost("/answer", AnswerAsync).RequireRateLimiting(SurveyPolicy);
    }

    private static async Task<IResult> ViewAsync(
        string tenant, PublicSurveyTokenRequest request, HttpContext http, IPublicTenantScopes scopes, CancellationToken ct)
    {
        if (Guard(http) is { } rejected)
        {
            return rejected;
        }

        if (await scopes.OpenAsync(tenant, SurveyIdentity, write: false, http.TraceIdentifier, ct) is not { } opened)
        {
            return Unknown();
        }

        await using var scope = opened;
        var result = await scope.ServiceProvider.GetRequiredService<ISurveyPortal>().ViewAsync(request.Token, ct);
        return result.IsSuccess ? Results.Ok(Map<PublicSurveyView>(result.Value!)) : Failure(result);
    }

    private static async Task<IResult> AnswerAsync(
        string tenant, PublicSurveyAnswerRequest request, HttpContext http, IPublicTenantScopes scopes, CancellationToken ct)
    {
        if (Guard(http) is { } rejected)
        {
            return rejected;
        }

        if (await scopes.OpenAsync(tenant, SurveyIdentity, write: true, http.TraceIdentifier, ct) is not { } opened)
        {
            return Unknown();
        }

        await using var scope = opened;
        var result = await scope.ServiceProvider.GetRequiredService<ISurveyPortal>().AnswerAsync(request.Token, request.Rating, request.Comment, ct);
        return result.IsSuccess ? Results.Ok(Map<PublicSurveyView>(result.Value!)) : Failure(result);
    }

    private static IResult? Guard(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
        {
            sizeLimit.MaxRequestBodySize = SurveyBodyMaxBytes;
        }

        return http.Request.ContentLength > SurveyBodyMaxBytes ? Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Gövde çok büyük.") : null;
    }

    private static IResult Unknown() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Bağlantı geçersiz.");

    private static IResult Failure<T>(SurveyPortalResult<T> result) => result.Error switch
    {
        SurveyPortalError.Invalid => Results.ValidationProblem(
            (result.Fields ?? new Dictionary<string, string>()).ToDictionary(f => f.Key, f => new[] { f.Value }), title: result.Message, statusCode: StatusCodes.Status400BadRequest),
        SurveyPortalError.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: result.Message ?? "Anket durumu değişti."),
        _ => Unknown(),
    };

    private static T Map<T>(object value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
}
