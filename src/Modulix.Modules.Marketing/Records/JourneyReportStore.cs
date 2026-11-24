using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Marketing.Domain;

namespace Modulix.Modules.Marketing.Records;

/// <summary>Bir içerik için bir sonuç ve neden kodundaki ileti sayısı.</summary>
internal sealed record MessageCountRow(Guid ContentId, string Outcome, string? Code, int Count);

/// <summary>
/// Yolculuk raporunun okuma deposu (ADR-0056 dilim 4): kayıt ve ileti sayımlarını yalnızca sayı olarak (kişi, adres ya da hedef kimliği olmadan) toplar.
/// Yazmaz; yolculuğun görünürlüğü ve izin denetimi çağıran serviste kayıt boru hattıyla yapılır.
/// </summary>
internal sealed class JourneyReportStore(IModuleData data)
{
    public async Task<Dictionary<string, int>> StatusCountsAsync(Guid journeyId, CancellationToken cancellationToken) =>
        (await data.Set<JourneyEnrollment>().AsNoTracking().Where(e => e.JourneyId == journeyId)
            .GroupBy(e => e.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
        .ToDictionary(r => r.Status, r => r.Count);

    /// <summary>Sona ermiş (<c>Exited</c>) kayıtların çıkış nedenine göre sayısı; neden boşsa boş dize.</summary>
    public async Task<IReadOnlyList<(string Reason, int Count)>> ExitReasonCountsAsync(Guid journeyId, CancellationToken cancellationToken) =>
        [.. (await data.Set<JourneyEnrollment>().AsNoTracking().Where(e => e.JourneyId == journeyId && e.Status == EnrollmentStatuses.Exited)
            .GroupBy(e => e.ExitReason).Select(g => new { Reason = g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            .Select(r => (r.Reason ?? string.Empty, r.Count))];

    public async Task<IReadOnlyList<MessageCountRow>> MessageCountsAsync(Guid journeyId, CancellationToken cancellationToken) =>
        [.. (await data.Set<JourneyMessage>().AsNoTracking().Where(m => m.JourneyId == journeyId)
            .GroupBy(m => new { m.ContentId, m.Outcome, m.Code }).Select(g => new { g.Key.ContentId, g.Key.Outcome, g.Key.Code, Count = g.Count() })
            .ToListAsync(cancellationToken)).Select(r => new MessageCountRow(r.ContentId, r.Outcome, r.Code, r.Count))];

    /// <summary>İçeriklerin konusu (adlandırma için); içerik silinmişse listede yoktur.</summary>
    public async Task<Dictionary<Guid, string?>> ContentSubjectsAsync(IReadOnlyCollection<Guid> contentIds, CancellationToken cancellationToken)
    {
        var ids = contentIds.ToList();
        return (await data.Set<CampaignContent>().AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.Subject }).ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id, c => c.Subject);
    }
}
