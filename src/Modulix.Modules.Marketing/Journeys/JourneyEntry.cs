using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Events;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Marketing.Contracts;
using Modulix.Modules.Marketing.Domain;
using Modulix.Modules.Marketing.Records;
using Modulix.Modules.Marketing.Segments;

namespace Modulix.Modules.Marketing.Journeys;

/// <param name="Entered">Bu turda açılan yeni kayıt sayısı.</param>
/// <param name="Remaining">Tur sınırı yüzünden bekleyen yeni giren sayısı (sonraki turda).</param>
/// <param name="Error">Segment değerlendirilemediyse ya da kayıt açılamadıysa nedeni (günlüğe yazılır, yolculuk sürer).</param>
public sealed record JourneyEntryOutcome(Guid JourneyId, int Entered, int Remaining, string? Error);

/// <summary>
/// Yolculuk girişi (ADR-0056 dilim 1b-2): etkin yolculuğun giriş segmentinin güncel üyelerinden kaydı olmayanlar için yolculuk kaydı açar ve
/// <c>marketing.journey.entered</c> olayını kayıtla aynı işlemde yayınlar. Aynı hedef yolculuğa bir kez girer (yeniden giriş politikası çıkış ve
/// tamamlanma geldiğinde, dilim 2); iş tekrar çalışsa yeni kayıt açmaz. Çağıran, segmenti değerlendirecek izinlerle (etkinleştirenin okuma izinleri)
/// çalışmalıdır.
/// </summary>
internal interface IJourneyEntry
{
    Task<JourneyEntryOutcome> EnterAsync(Guid journeyId, int maxEntries, CancellationToken cancellationToken);
}

internal sealed class JourneyEntry(
    JourneyEntryStore store, ISegmentService segments, IRecordPipeline pipeline, SegmentWorkflowGate gate, IBusinessEventPublisher events, TimeProvider time) : IJourneyEntry
{
    /// <summary>Bir yolculuğun girişinde işlenebilecek en büyük segment (hedef kitle sınırıyla aynı).</summary>
    public const int MaxTargets = 20000;

    private static readonly IReadOnlyList<string> EntryPermissions = [MarketingModule.Permissions.JourneysWrite];

    public async Task<JourneyEntryOutcome> EnterAsync(Guid journeyId, int maxEntries, CancellationToken cancellationToken)
    {
        if (await store.FindActiveAsync(journeyId, cancellationToken) is not { } journey)
        {
            return new JourneyEntryOutcome(journeyId, 0, 0, null);
        }

        if (!Enum.TryParse<SegmentTarget>(journey.Target, ignoreCase: true, out var target))
        {
            return new JourneyEntryOutcome(journeyId, 0, 0, $"Hedef türü geçersiz: {journey.Target}.");
        }

        var members = await segments.MemberIdsAsync(journey.SegmentId, target, MaxTargets, cancellationToken);
        if (!members.IsSuccess)
        {
            return new JourneyEntryOutcome(journeyId, 0, 0, members.Error!.Message);
        }

        var enrolled = await store.EnrolledTargetIdsAsync(journeyId, cancellationToken);
        var fresh = members.Value!.Where(id => !enrolled.Contains(id)).ToList();
        var entered = 0;
        using var open = gate.Open(EntryPermissions);
        foreach (var targetId in fresh.Take(maxEntries))
        {
            var enteredAt = time.GetUtcNow();
            var result = await pipeline.RunAtomicAsync(async token =>
            {
                var created = await pipeline.CreateAsync(
                    new RecordCommand(MarketingModule.Entities.JourneyEnrollment, null, new JsonObject
                    {
                        ["journeyId"] = journeyId.ToString(),
                        ["targetId"] = targetId.ToString(),
                        ["status"] = EnrollmentStatuses.Active,
                        ["enteredAt"] = enteredAt.ToString("O"),
                    }),
                    token);
                if (!created.IsSuccess)
                {
                    return created;
                }

                await events.PublishAsync(
                    new BusinessEvent(MarketingModule.Events.JourneyEntered, created.Value!.Id, new JsonObject
                    {
                        ["journeyId"] = journeyId.ToString(),
                        ["targetId"] = targetId.ToString(),
                        ["enteredAt"] = enteredAt.ToString("O"),
                        ["target"] = journey.Target,
                    })
                    { RecordVersion = created.Value.Version },
                    token);
                return created;
            }, cancellationToken);

            if (result.IsSuccess)
            {
                entered++;
            }
            else if (result.Error!.Code != RecordErrorCode.Conflict)
            {
                // Bir hedef yazılamazsa tur durur (sonraki tur yeniden dener); çakışma (başka işleyici önce girdi) atlanır.
                return new JourneyEntryOutcome(journeyId, entered, fresh.Count - entered, result.Error.Message);
            }
        }

        return new JourneyEntryOutcome(journeyId, entered, Math.Max(0, fresh.Count - maxEntries), null);
    }
}
