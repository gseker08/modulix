using Modulix.Core.Contracts.Authorization;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Marketing.Domain;
using Modulix.Modules.Marketing.Records;

namespace Modulix.Modules.Marketing.Journeys;

/// <param name="Reason">Kararlı neden kodu: <c>unsubscribed</c>, <c>exit-condition</c>, <c>manual</c> (elle çıkarma; serbest gerekçe metni raporda gösterilmez).</param>
public sealed record JourneyExitReasonCount(string Reason, int Count);

/// <param name="Code">Atlanan iletinin kararlı neden kodu (izin kodları, <c>frequency-limit</c>, <c>no-matching-contact</c> vb.).</param>
public sealed record JourneySkipReasonCount(string Code, int Count);

/// <summary>İletilerin sonuç dağılımı. <c>Unknown</c>, sağlayıcı sonucu belirsiz olanları ve yarım kalmış (<c>Processing</c>) denemeleri içerir.</summary>
public sealed record JourneyMessageTotals(int Total, int Accepted, int Skipped, int Failed, int Unknown, IReadOnlyList<JourneySkipReasonCount> SkipReasons);

/// <summary>Bir içeriğin (adımın) dönüşümü: kaç kişiye gönderildi, kaçı atlandı. Aynı içerik birden çok adımda kullanılırsa tek satırda toplanır.</summary>
public sealed record JourneyContentRow(Guid ContentId, string? Subject, int Total, int Accepted, int Skipped, int Failed, int Unknown);

/// <summary>Yolculuk raporu (ADR-0056 dilim 4): yalnızca sayılar; kişi, adres ya da hedef kimliği içermez.</summary>
public sealed record JourneyReport(
    Guid JourneyId, int Enrolled, int Active, int Completed, int Exited, IReadOnlyList<JourneyExitReasonCount> ExitReasons,
    JourneyMessageTotals Messages, IReadOnlyList<JourneyContentRow> Contents, DateTimeOffset GeneratedAt);

public interface IJourneyReports
{
    /// <summary>
    /// Yolculuğun kayıt, çıkış nedeni ve ileti sayımları. <c>marketing.reports.read</c> gerekir ve yolculuk çağıranın görebildiği (izin ve veri kapsamı) bir yolculuk olmalıdır;
    /// aksi hâlde "bulunamadı".
    /// </summary>
    Task<RecordResult<JourneyReport>> ReportAsync(Guid journeyId, CancellationToken cancellationToken);
}

internal sealed class JourneyReports(IRecordPipeline pipeline, JourneyReportStore store, ICurrentAccess permissions, TimeProvider time) : IJourneyReports
{
    private static readonly HashSet<string> KnownExitReasons = [EnrollmentReasons.Unsubscribed, EnrollmentReasons.ExitCondition];

    public async Task<RecordResult<JourneyReport>> ReportAsync(Guid journeyId, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(MarketingModule.Permissions.ReportsRead, cancellationToken))
        {
            return RecordResult<JourneyReport>.Fail(
                RecordErrorCode.PermissionDenied, ServerMessages.Access.PermissionRequired(MarketingModule.Permissions.ReportsRead));
        }

        // Yolculuk kayıt boru hattından okunur: görme izni ve veri kapsamı uygulanır; görünmeyen yolculuk "bulunamadı"dır.
        var loaded = await pipeline.GetAsync(new RecordRef(MarketingModule.Entities.Journey, journeyId), cancellationToken);
        if (!loaded.IsSuccess)
        {
            return RecordResult<JourneyReport>.Fail(loaded.Error!.Code, loaded.Error.Message, loaded.Error.FieldErrors);
        }

        var statuses = await store.StatusCountsAsync(journeyId, cancellationToken);
        int Count(string status) => statuses.GetValueOrDefault(status);

        // Serbest metin gerekçeler ("elle çıkarma") tek kodda toplanır: rapor kişisel bilgi taşıyabilecek metni göstermez.
        var exits = (await store.ExitReasonCountsAsync(journeyId, cancellationToken))
            .GroupBy(r => KnownExitReasons.Contains(r.Reason) ? r.Reason : "manual")
            .Select(g => new JourneyExitReasonCount(g.Key, g.Sum(r => r.Count)))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Reason, StringComparer.Ordinal).ToList();

        var rows = await store.MessageCountsAsync(journeyId, cancellationToken);
        int Total(Func<MessageCountRow, bool> where) => rows.Where(where).Sum(r => r.Count);
        bool IsUnknown(MessageCountRow r) => r.Outcome is JourneyMessageOutcomes.Unknown or JourneyMessageOutcomes.Processing;
        var skipReasons = rows.Where(r => r.Outcome == JourneyMessageOutcomes.Skipped)
            .GroupBy(r => r.Code ?? string.Empty)
            .Select(g => new JourneySkipReasonCount(g.Key, g.Sum(r => r.Count)))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Code, StringComparer.Ordinal).ToList();
        var messages = new JourneyMessageTotals(
            Total(_ => true), Total(r => r.Outcome == JourneyMessageOutcomes.Accepted), Total(r => r.Outcome == JourneyMessageOutcomes.Skipped),
            Total(r => r.Outcome == JourneyMessageOutcomes.Failed), Total(IsUnknown), skipReasons);

        var subjects = await store.ContentSubjectsAsync([.. rows.Select(r => r.ContentId).Distinct()], cancellationToken);
        var contents = rows.GroupBy(r => r.ContentId).Select(g => new JourneyContentRow(
            g.Key, subjects.GetValueOrDefault(g.Key), g.Sum(r => r.Count),
            g.Where(r => r.Outcome == JourneyMessageOutcomes.Accepted).Sum(r => r.Count),
            g.Where(r => r.Outcome == JourneyMessageOutcomes.Skipped).Sum(r => r.Count),
            g.Where(r => r.Outcome == JourneyMessageOutcomes.Failed).Sum(r => r.Count),
            g.Where(IsUnknown).Sum(r => r.Count)))
            .OrderByDescending(c => c.Total).ThenBy(c => c.ContentId).ToList();

        return RecordResult<JourneyReport>.Ok(new JourneyReport(
            journeyId, statuses.Values.Sum(), Count(EnrollmentStatuses.Active), Count(EnrollmentStatuses.Completed), Count(EnrollmentStatuses.Exited),
            exits, messages, contents, time.GetUtcNow()));
    }
}
