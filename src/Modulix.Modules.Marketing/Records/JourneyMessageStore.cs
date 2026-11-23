using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Marketing.Contracts;
using Modulix.Modules.Marketing.Domain;

namespace Modulix.Modules.Marketing.Records;

/// <summary>Yolculuk kaydının gönderim için bilinmesi gerekenleri: hedef, durumları ve yolculuğun hedef türü.</summary>
internal sealed record EnrollmentSendInfo(Guid EnrollmentId, Guid JourneyId, Guid TargetId, string EnrollmentStatus, string JourneyStatus, SegmentTarget Target);

/// <summary>Bir adımın daha önce yazılmış iletisi (idempotent yeniden çağrı için).</summary>
internal sealed record JourneyMessageInfo(string Outcome, string? Code);

/// <summary>
/// Yolculuk iletisi göndericisinin okuma deposu (ADR-0056 dilim 2a): kayıt ve adım için mevcut iletiyi, kaydı ve yolculuğu okur. Yazmaz; günlük satırı
/// <see cref="IJournalWriter"/> ile yazılır.
/// </summary>
internal sealed class JourneyMessageStore(IModuleData data)
{
    public async Task<EnrollmentSendInfo?> FindEnrollmentAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var row = await (
            from e in data.Set<JourneyEnrollment>().AsNoTracking()
            join j in data.Set<Journey>().AsNoTracking() on e.JourneyId equals j.Id
            where e.Id == enrollmentId
            select new { e.Id, e.JourneyId, e.TargetId, EnrollmentStatus = e.Status, JourneyStatus = j.Status, j.Target })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null || !Enum.TryParse<SegmentTarget>(row.Target, ignoreCase: true, out var target)
            ? null
            : new EnrollmentSendInfo(row.Id, row.JourneyId, row.TargetId, row.EnrollmentStatus, row.JourneyStatus, target);
    }

    public async Task<JourneyMessageInfo?> FindMessageAsync(Guid enrollmentId, string stepKey, CancellationToken cancellationToken) =>
        await data.Set<JourneyMessage>().AsNoTracking().Where(m => m.EnrollmentId == enrollmentId && m.StepKey == stepKey)
            .Select(m => new JourneyMessageInfo(m.Outcome, m.Code)).SingleOrDefaultAsync(cancellationToken);
}
