using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Marketing.Domain;
using Modulix.Modules.Marketing.Records;

namespace Modulix.Modules.Marketing.Journeys;

/// <summary>
/// Yolculuk durum geçişleri (ADR-0056 dilim 1b): yayın, duraklatma, sürdürme ve arşiv. Durum genel kayıt uçlarından yazılamaz; yalnızca bu servis
/// iş akışı kapısını <c>marketing.journeys.publish</c> izniyle açıp kayıt boru hattından yazar (izin, veri kapsamı ve denetim kaydı boru hattındadır).
/// </summary>
public interface IJourneyService
{
    /// <summary>Taslağı etkinleştirir; giriş segmenti etkin ve yolculukla aynı hedef türünde olmalıdır.</summary>
    Task<RecordResult<RecordData>> PublishAsync(Guid journeyId, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Etkin yolculuğu duraklatır; mevcut kayıtlar korunur.</summary>
    Task<RecordResult<RecordData>> PauseAsync(Guid journeyId, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Duraklatılmış yolculuğu sürdürür; giriş segmenti hâlâ etkin olmalıdır.</summary>
    Task<RecordResult<RecordData>> ResumeAsync(Guid journeyId, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Yolculuğu arşivler (geri alınmaz); arşivlenmiş yolculuk yeni giriş almaz.</summary>
    Task<RecordResult<RecordData>> ArchiveAsync(Guid journeyId, long? expectedVersion, CancellationToken cancellationToken);
}

internal sealed class JourneyService(IRecordPipeline pipeline, SegmentWorkflowGate gate) : IJourneyService
{
    private static readonly IReadOnlyList<string> PublishPermissions = [MarketingModule.Permissions.JourneysPublish];

    public Task<RecordResult<RecordData>> PublishAsync(Guid journeyId, long? expectedVersion, CancellationToken cancellationToken) =>
        ChangeAsync(journeyId, expectedVersion, JourneyStatuses.Active, [JourneyStatuses.Draft], MarketingMessages.Journey.OnlyDraftPublish, cancellationToken);

    public Task<RecordResult<RecordData>> PauseAsync(Guid journeyId, long? expectedVersion, CancellationToken cancellationToken) =>
        ChangeAsync(journeyId, expectedVersion, JourneyStatuses.Paused, [JourneyStatuses.Active], MarketingMessages.Journey.OnlyActivePause, cancellationToken);

    public Task<RecordResult<RecordData>> ResumeAsync(Guid journeyId, long? expectedVersion, CancellationToken cancellationToken) =>
        ChangeAsync(journeyId, expectedVersion, JourneyStatuses.Active, [JourneyStatuses.Paused], MarketingMessages.Journey.OnlyPausedResume, cancellationToken);

    public Task<RecordResult<RecordData>> ArchiveAsync(Guid journeyId, long? expectedVersion, CancellationToken cancellationToken) =>
        ChangeAsync(
            journeyId, expectedVersion, JourneyStatuses.Archived, [JourneyStatuses.Draft, JourneyStatuses.Active, JourneyStatuses.Paused],
            MarketingMessages.Journey.AlreadyArchived, cancellationToken);

    private async Task<RecordResult<RecordData>> ChangeAsync(
        Guid journeyId, long? expectedVersion, string target, string[] allowedFrom, Modulix.Core.Contracts.Localization.ServerMessage notAllowed,
        CancellationToken cancellationToken)
    {
        // Kayıt boru hattından okunur: yolculuğu görme yetkisi ve veri kapsamı uygulanır; görünmeyen yolculuk "bulunamadı"dır.
        var loaded = await pipeline.GetAsync(new RecordRef(MarketingModule.Entities.Journey, journeyId), cancellationToken);
        if (!loaded.IsSuccess)
        {
            return loaded;
        }

        var status = loaded.Value!.Values["status"]?.GetValue<string>();
        if (status is null || !allowedFrom.Contains(status))
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.Conflict, notAllowed);
        }

        using var open = gate.Open(PublishPermissions);
        return await pipeline.UpdateAsync(
            new RecordCommand(MarketingModule.Entities.Journey, journeyId, new JsonObject { ["status"] = target }, expectedVersion), cancellationToken);
    }
}
