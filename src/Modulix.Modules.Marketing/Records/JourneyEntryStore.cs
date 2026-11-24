using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Marketing.Domain;

namespace Modulix.Modules.Marketing.Records;

/// <summary>Giriş işinin bir yolculuk hakkında bildiği: segment, hedef türü ve etkinleştirenin okuma izinleri.</summary>
internal sealed record ActiveJourney(Guid Id, string Code, Guid SegmentId, string Target, string ReentryPolicy, IReadOnlyList<string> Permissions);

/// <summary>Bir hedefin yolculuktaki son kaydı (yeniden giriş kararı için).</summary>
internal sealed record LatestEnrollment(Guid Id, Guid TargetId, string Status, DateTimeOffset EnteredAt, DateTimeOffset? SegmentLeftAt);

/// <summary>
/// Yolculuk giriş işinin okuma deposu (ADR-0056 dilim 1b-2). İş kimliği kayıt boru hattından okuyamayacağı alanlara (etkinleştirenin izinleri) ihtiyaç duyar;
/// bu okuma burada, kayıt depolarıyla aynı klasörde yapılır. Yazmaz.
/// </summary>
internal sealed class JourneyEntryStore(IModuleData data)
{
    public async Task<IReadOnlyList<ActiveJourney>> ListActiveAsync(CancellationToken cancellationToken) =>
        [.. (await data.Set<Journey>().AsNoTracking().Where(j => j.Status == JourneyStatuses.Active).OrderBy(j => j.Code)
            .Select(j => new { j.Id, j.Code, j.SegmentId, j.Target, j.ReentryPolicy, j.EntryPermissions }).ToListAsync(cancellationToken))
            .Select(j => new ActiveJourney(j.Id, j.Code, j.SegmentId, j.Target, j.ReentryPolicy, EntryPermissionSet.Parse(j.EntryPermissions)))];

    public async Task<ActiveJourney?> FindActiveAsync(Guid journeyId, CancellationToken cancellationToken)
    {
        var journey = await data.Set<Journey>().AsNoTracking().Where(j => j.Id == journeyId && j.Status == JourneyStatuses.Active)
            .Select(j => new { j.Id, j.Code, j.SegmentId, j.Target, j.ReentryPolicy, j.EntryPermissions }).SingleOrDefaultAsync(cancellationToken);
        return journey is null
            ? null
            : new ActiveJourney(journey.Id, journey.Code, journey.SegmentId, journey.Target, journey.ReentryPolicy, EntryPermissionSet.Parse(journey.EntryPermissions));
    }

    /// <summary>Yolculukta kaydı olan her hedefin <b>son</b> kaydı (durumdan bağımsız): yeni giriş ve yeniden giriş kararı buna bakar.</summary>
    public async Task<Dictionary<Guid, LatestEnrollment>> LatestEnrollmentsAsync(Guid journeyId, CancellationToken cancellationToken) =>
        (await data.Set<JourneyEnrollment>().AsNoTracking().Where(e => e.JourneyId == journeyId)
            .Select(e => new LatestEnrollment(e.Id, e.TargetId, e.Status, e.EnteredAt, e.SegmentLeftAt)).ToListAsync(cancellationToken))
        .GroupBy(e => e.TargetId)
        .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.EnteredAt).First());
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
