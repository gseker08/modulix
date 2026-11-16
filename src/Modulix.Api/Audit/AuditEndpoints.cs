using System.Globalization;
using System.Text;
using Modulix.Api.Access;
using Modulix.Core.Audit;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Modules;
using Modulix.Core.Records;
using Modulix.Core.Settings;
using Modulix.Shared.Audit;

namespace Modulix.Api.Audit;

/// <summary>
/// Denetim kaydı (Y5): /api/audit. Hepsi aktif tenant ve <c>core.audit.read</c> ister. Yalnızca okuma uç noktaları vardır;
/// denetim kaydını değiştiren veya silen bir yol yoktur. Kurallar <see cref="IAuditLog"/>'dadır.
/// </summary>
public static class AuditEndpoints
{
    /// <summary>Bir dışa aktarmadaki en fazla kayıt; fazlası için tarih aralığı daraltılır.</summary>
    public const int ExportLimit = 50_000;


    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var audit = app.MapGroup("/api/audit")
            .RequireAuthorization()
            .RequirePermission(CoreModule.Permissions.ReadAudit)
            .WithTags("Audit");

        audit.MapGet("/", ListAsync);
        audit.MapGet("/filters", GetFiltersAsync);
        audit.MapGet("/export", ExportAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid? userId, string? entityKey, AuditOperation? action, AuditSource? source, DateOnly? from, DateOnly? to, int? page, string? sort,
        IAuditLog log, ITenantSettings settings, CancellationToken ct)
    {
        // Başlığa göre sıralama (ADR-0036): yalnızca bilinen anahtarlar kabul edilir.
        if (!string.IsNullOrEmpty(sort) && !AuditQuery.SortKeys.Contains(sort))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["sort"] = ["Geçersiz sıralama."] }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var zone = await ZoneAsync(settings, ct);
        var query = Query(userId, entityKey, action, source, from, to, page ?? 1, zone) with { Sort = string.IsNullOrEmpty(sort) ? null : sort };
        var result = await log.QueryAsync(query, ct);
        return Results.Ok(ToResponse(result));
    }

    internal static AuditPageResponse ToResponse(AuditLogPage page) =>
        new(page.Items.Select(ToDto).ToList(), page.Page, page.PageSize, page.TotalCount);

    private static async Task<AuditFiltersResponse> GetFiltersAsync(
        IAuditLog log, ModuleCatalog catalog, IRequestLanguage language, ITextCatalog texts, CancellationToken ct)
    {
        var bundle = texts.Bundle(await language.GetAsync(ct));
        return new(
            (await log.ListActorsAsync(ct)).Select(a => new AuditActorDto(a.UserId, a.Name)).ToList(),
            catalog.AuditedEntities
                .Select(e => new AuditEntityTypeDto(e.EntityKey, MetadataTexts.Text(bundle, MetadataTexts.EntityKey(e.EntityKey), e.DisplayName)))
                .ToList());
    }

    // CSV'nin sabit metinleri okurun dilinde (ADR-0034, dilim 18c); işlem ve kaynak adları denetim ekranıyla aynı anahtarlardan gelir.
    private static string ActionText(IReadOnlyDictionary<string, string> bundle, AuditOperation action) =>
        MetadataTexts.Text(bundle, "admin.audit-action." + action.ToString().ToLowerInvariant(), AuditText.Of(action));

    private static string SourceText(IReadOnlyDictionary<string, string> bundle, AuditSource source, string? subscription)
    {
        var text = MetadataTexts.Text(bundle, "admin.audit-source." + source.ToString().ToLowerInvariant(), AuditText.Of(source));
        return subscription is null ? text : $"{text} · {subscription}";
    }

    /// <summary>
    /// CSV: noktalı virgülle ayrılmış, UTF-8 BOM'lu (Türkçe Excel doğrudan açar); her değişen alan bir satır. Formül gibi
    /// başlayan hücreler kesme işaretiyle başlatılır (CSV enjeksiyonu).
    /// </summary>
    private static async Task<IResult> ExportAsync(
        Guid? userId, string? entityKey, AuditOperation? action, AuditSource? source, DateOnly? from, DateOnly? to,
        IAuditLog log, ITenantSettings settings, IRequestLanguage language, ITextCatalog texts, CancellationToken ct)
    {
        var zone = await ZoneAsync(settings, ct);
        var bundle = texts.Bundle(await language.GetAsync(ct));
        string Text(string key, string source) => MetadataTexts.Text(bundle, key, source);
        var hiddenText = Text("common.history.hidden", AuditText.Hidden);
        IReadOnlyList<AuditLogItem> items;
        try
        {
            items = await log.ExportAsync(Query(userId, entityKey, action, source, from, to, 1, zone), ExportLimit, ct);
        }
        catch (AuditExportTooLargeException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        var csv = new StringBuilder();
        AppendRow(csv,
        [
            Text("audit.csv.date", "Tarih"),
            Text("audit.csv.user", "Kullanıcı"),
            Text("audit.csv.email", "E-posta"),
            Text("audit.csv.entity", "Kayıt türü"),
            Text("audit.csv.record", "Kayıt"),
            Text("audit.csv.action", "İşlem"),
            Text("audit.csv.source", "Kaynak"),
            Text("audit.csv.field", "Alan"),
            Text("audit.csv.old", "Eski değer"),
            Text("audit.csv.new", "Yeni değer"),
        ]);
        foreach (var item in items)
        {
            string?[] common =
            [
                RegionalTime.ToLocal(item.OccurredAt, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                item.UserName,
                item.UserEmail,
                item.EntityName,
                item.ValuesHidden ? hiddenText : item.RecordTitle,
                ActionText(bundle, (AuditOperation)item.Action),
                SourceText(bundle, (AuditSource)item.Source, item.Subscription),
            ];

            if (item.Changes.Count == 0)
            {
                AppendRow(csv, [.. common, null, null, null]);
            }

            foreach (var change in item.Changes)
            {
                AppendRow(csv, item.ValuesHidden
                    ? [.. common, change.Label, hiddenText, hiddenText]
                    : [.. common, change.Label, change.Old, change.New]);
            }
        }

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(csv.ToString()))
            .ToArray();
        var name = FormattableString.Invariant($"denetim-kaydi-{RegionalTime.ToLocal(DateTimeOffset.UtcNow, zone):yyyy-MM-dd}.csv");
        return Results.File(bytes, "text/csv; charset=utf-8", name);
    }

    private static AuditQuery Query(
        Guid? userId, string? entityKey, AuditOperation? action, AuditSource? source, DateOnly? from, DateOnly? to, int page,
        TimeZoneInfo zone) =>
        new(
            userId,
            entityKey,
            (AuditAction?)action,
            (ExecutionSource?)source,
            from is { } start ? RegionalTime.StartOfDay(start, zone) : null,
            to is { } end ? RegionalTime.StartOfDay(end.AddDays(1), zone) : null,
            Page: page);

    // Tarih filtresi gün olarak gelir; günler ve CSV'deki saatler firmanın saat dilimindedir (Y6b).
    private static async Task<TimeZoneInfo> ZoneAsync(ITenantSettings settings, CancellationToken ct) =>
        RegionalTime.Find(await settings.GetValueAsync(CoreModule.SettingKeys.TimeZone, ct));

    private static AuditEntryDto ToDto(AuditLogItem item) => new(
        item.Id,
        item.OccurredAt,
        item.UserId,
        item.UserName,
        item.EntityKey,
        item.EntityName,
        item.RecordId,
        item.RecordTitle,
        (AuditOperation)item.Action,
        (AuditSource)item.Source,
        item.ValuesHidden,
        item.Changes.Select(c => new AuditFieldChangeDto(c.Field, c.Label, c.Old, c.New, c.Hidden)).ToList(),
        item.Subscription);

    private static void AppendRow(StringBuilder csv, IEnumerable<string?> cells)
    {
        csv.Append(string.Join(';', cells.Select(Cell)));
        csv.Append("\r\n");
    }

    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
