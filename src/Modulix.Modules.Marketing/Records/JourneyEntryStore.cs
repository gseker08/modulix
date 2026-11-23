using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Marketing.Domain;

namespace Modulix.Modules.Marketing.Records;

/// <summary>Giriş işinin bir yolculuk hakkında bildiği: segment, hedef türü ve etkinleştirenin okuma izinleri.</summary>
internal sealed record ActiveJourney(Guid Id, string Code, Guid SegmentId, string Target, IReadOnlyList<string> Permissions);

/// <summary>
/// Yolculuk giriş işinin okuma deposu (ADR-0056 dilim 1b-2). İş kimliği kayıt boru hattından okuyamayacağı alanlara (etkinleştirenin izinleri) ihtiyaç duyar;
/// bu okuma burada, kayıt depolarıyla aynı klasörde yapılır. Yazmaz.
/// </summary>
internal sealed class JourneyEntryStore(IModuleData data)
{
    public async Task<IReadOnlyList<ActiveJourney>> ListActiveAsync(CancellationToken cancellationToken) =>
        [.. (await data.Set<Journey>().AsNoTracking().Where(j => j.Status == JourneyStatuses.Active).OrderBy(j => j.Code)
            .Select(j => new { j.Id, j.Code, j.SegmentId, j.Target, j.EntryPermissions }).ToListAsync(cancellationToken))
            .Select(j => new ActiveJourney(j.Id, j.Code, j.SegmentId, j.Target, EntryPermissionSet.Parse(j.EntryPermissions)))];

    public async Task<ActiveJourney?> FindActiveAsync(Guid journeyId, CancellationToken cancellationToken)
    {
        var journey = await data.Set<Journey>().AsNoTracking().Where(j => j.Id == journeyId && j.Status == JourneyStatuses.Active)
            .Select(j => new { j.Id, j.Code, j.SegmentId, j.Target, j.EntryPermissions }).SingleOrDefaultAsync(cancellationToken);
        return journey is null ? null : new ActiveJourney(journey.Id, journey.Code, journey.SegmentId, journey.Target, EntryPermissionSet.Parse(journey.EntryPermissions));
    }

    /// <summary>Yolculukta daha önce kaydı olan hedefler (durumdan bağımsız): yeniden giriş politikası çıkış ve tamamlanma geldiğinde (dilim 2) uygulanır.</summary>
    public async Task<HashSet<Guid>> EnrolledTargetIdsAsync(Guid journeyId, CancellationToken cancellationToken) =>
        [.. await data.Set<JourneyEnrollment>().AsNoTracking().Where(e => e.JourneyId == journeyId).Select(e => e.TargetId).ToListAsync(cancellationToken)];
}

/// <summary>Yolculuğun saklanan giriş izinleri: yalnızca okuma izinleri (<c>.read</c>), sıralı JSON dizi.</summary>
internal static class EntryPermissionSet
{
    public static string Serialize(IEnumerable<string> permissions) =>
        JsonSerializer.Serialize(permissions.Where(p => p.EndsWith(".read", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal));

    public static IReadOnlyList<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) is { } list ? [.. list.Where(p => p.EndsWith(".read", StringComparison.Ordinal))] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
