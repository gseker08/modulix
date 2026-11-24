using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Marketing.Domain;
using Modulix.Modules.Marketing.Records;

namespace Modulix.Modules.Marketing.Journeys;

/// <summary>
/// Yolculuk kaydının sona ermesi (ADR-0056 dilim 2c-1): elle çıkarma, abonelikten çıkma ya da kuralın sonundaki "tamamla" adımıyla kapanış. Kayıt yalnızca etkinken
/// sona erer (<see cref="EnrollmentStatuses.Active"/> → <c>Exited</c> ya da <c>Completed</c>); sona eren kayıt yeniden etkinleşmez. Durum genel kayıt uçlarından yazılamaz,
/// bu servis iş akışı kapısını <c>marketing.journeys.write</c> izniyle açıp kayıt boru hattından yazar (izin, veri kapsamı, sürüm ve denetim kaydı boru hattındadır).
/// </summary>
public interface IJourneyEnrollments
{
    /// <summary>Kullanıcının kaydı elle çıkarması; gerekçe zorunludur (en çok <see cref="JourneyEnrollment.ExitReasonMaxLength"/> karakter).</summary>
    Task<RecordResult<RecordData>> ExitAsync(Guid enrollmentId, string? reason, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Sistem çıkışı (ör. abonelikten çıkma): kararlı neden koduyla.</summary>
    Task<RecordResult<RecordData>> ExitBySystemAsync(Guid enrollmentId, string reasonCode, CancellationToken cancellationToken);

    /// <summary>Yolculuğun tamamlanması (kuralın "yolculuğu tamamla" adımı).</summary>
    Task<RecordResult<RecordData>> CompleteAsync(Guid enrollmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Etkin kayda yanıt işareti koyar (ADR-0056 dilim 3): kuralın adım koşulu bu alana bakarak dallanır. Yalnızca <see cref="ResponseKinds.JourneyKinds"/> türleri kabul edilir;
    /// çıkış talebi iletişim tercihlerinden kaydedilir ve yolculuk kendiliğinden sona erer.
    /// </summary>
    Task<RecordResult<RecordData>> RegisterResponseAsync(Guid enrollmentId, string? kind, long? expectedVersion, CancellationToken cancellationToken);
}

/// <summary>Sistem çıkışı ve tamamlanma için kararlı neden kodları.</summary>
public static class EnrollmentReasons
{
    /// <summary>Alıcı pazarlama iletişiminden çıktı (izin geri alındı, konudan ya da tüm pazarlamadan çıktı, şikâyet).</summary>
    public const string Unsubscribed = "unsubscribed";

    /// <summary>Yolculuğun çıkış koşulu sağlandı (ör. hedef fırsata dönüştü).</summary>
    public const string ExitCondition = "exit-condition";

    /// <summary>Kural akışının sonunda tamamlandı.</summary>
    public const string Completed = "completed";
}

internal sealed class JourneyEnrollmentService(IRecordPipeline pipeline, SegmentWorkflowGate gate, TimeProvider time) : IJourneyEnrollments
{
    private static readonly IReadOnlyList<string> WritePermissions = [MarketingModule.Permissions.JourneysWrite];

    /// <summary>Yanıt işareti yolculuk yazma iznini şart koşmaz: kapı açıkken boru hattı bu izinlerden birini arar, yanıtı kaydeden kullanıcı yanıt iznine sahiptir.</summary>
    private static readonly IReadOnlyList<string> ResponsePermissions = [MarketingModule.Permissions.JourneysWrite, MarketingModule.Permissions.ResponsesWrite];

    public Task<RecordResult<RecordData>> ExitAsync(Guid enrollmentId, string? reason, long? expectedVersion, CancellationToken cancellationToken)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(Invalid(MarketingMessages.Journey.ExitReasonRequired));
        }

        if (text.Length > JourneyEnrollment.ExitReasonMaxLength)
        {
            return Task.FromResult(Invalid(MarketingMessages.Journey.ExitReasonTooLong(JourneyEnrollment.ExitReasonMaxLength)));
        }

        return EndAsync(enrollmentId, EnrollmentStatuses.Exited, text, expectedVersion, cancellationToken);
    }

    public Task<RecordResult<RecordData>> ExitBySystemAsync(Guid enrollmentId, string reasonCode, CancellationToken cancellationToken) =>
        EndAsync(enrollmentId, EnrollmentStatuses.Exited, reasonCode, null, cancellationToken);

    public Task<RecordResult<RecordData>> CompleteAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
        EndAsync(enrollmentId, EnrollmentStatuses.Completed, EnrollmentReasons.Completed, null, cancellationToken);

    public async Task<RecordResult<RecordData>> RegisterResponseAsync(Guid enrollmentId, string? kind, long? expectedVersion, CancellationToken cancellationToken)
    {
        if (kind is null || !ResponseKinds.JourneyKinds.Contains(kind))
        {
            var message = MarketingMessages.Journey.ResponseKindInvalid;
            return RecordResult<RecordData>.Fail(RecordErrorCode.ValidationFailed, message, [new RecordFieldError("kind", message)]);
        }

        var loaded = await pipeline.GetAsync(new RecordRef(MarketingModule.Entities.JourneyEnrollment, enrollmentId), cancellationToken);
        if (!loaded.IsSuccess)
        {
            return loaded;
        }

        if (loaded.Value!.Values["status"]?.GetValue<string>() != EnrollmentStatuses.Active)
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.Conflict, MarketingMessages.Journey.EnrollmentNotActive);
        }

        using var open = gate.Open(ResponsePermissions);
        return await pipeline.UpdateAsync(
            new RecordCommand(MarketingModule.Entities.JourneyEnrollment, enrollmentId, new JsonObject
            {
                ["lastResponseKind"] = kind,
                ["lastResponseAt"] = time.GetUtcNow().ToString("O"),
            }, expectedVersion),
            cancellationToken);
    }

    private async Task<RecordResult<RecordData>> EndAsync(
        Guid enrollmentId, string status, string reason, long? expectedVersion, CancellationToken cancellationToken)
    {
        // Kayıt boru hattından okunur: yolculuğu görme yetkisi ve veri kapsamı uygulanır; görünmeyen kayıt "bulunamadı"dır.
        var loaded = await pipeline.GetAsync(new RecordRef(MarketingModule.Entities.JourneyEnrollment, enrollmentId), cancellationToken);
        if (!loaded.IsSuccess)
        {
            return loaded;
        }

        if (loaded.Value!.Values["status"]?.GetValue<string>() != EnrollmentStatuses.Active)
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.Conflict, MarketingMessages.Journey.EnrollmentNotActive);
        }

        using var open = gate.Open(WritePermissions);
        return await pipeline.UpdateAsync(
            new RecordCommand(MarketingModule.Entities.JourneyEnrollment, enrollmentId, new JsonObject
            {
                ["status"] = status,
                ["exitedAt"] = time.GetUtcNow().ToString("O"),
                ["exitReason"] = reason,
            }, expectedVersion),
            cancellationToken);
    }

    private static RecordResult<RecordData> Invalid(Modulix.Core.Contracts.Localization.ServerMessage message) =>
        RecordResult<RecordData>.Fail(RecordErrorCode.ValidationFailed, message, [new RecordFieldError("reason", message)]);
}
