using Modulix.Core.Contracts.Authorization;
using Modulix.Core.Contracts.Communication;
using Modulix.Core.Contracts.Persistence;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Modules.Marketing.Contracts;
using Modulix.Modules.Marketing.Domain;
using Modulix.Modules.Marketing.Records;
using Modulix.Modules.Marketing.Runs;

namespace Modulix.Modules.Marketing.Journeys;

/// <param name="EnrollmentId">Yolculuk kaydı.</param>
/// <param name="ContentId">Gönderilecek içerik (kampanya içeriği kaydı; kanal ve konu kampanyasından gelir).</param>
/// <param name="StepKey">Adımın kararlı anahtarı; kayıt + adım başına tek ileti gider.</param>
/// <param name="Rule">Firma hedefinde alıcı kişinin seçimi (kişi ve lead hedefinde yok sayılır).</param>
public sealed record JourneySendRequest(Guid EnrollmentId, Guid ContentId, string StepKey, RecipientRule Rule = RecipientRule.PrimaryContact);

/// <param name="Outcome"><see cref="JourneyMessageOutcomes"/> değerlerinden biri.</param>
/// <param name="Code">Gönderilmeyen ya da başarısız iletinin kararlı neden kodu; kabul edilen iletide boş.</param>
/// <param name="AlreadyHandled">Bu adım için ileti daha önce işlenmişti; yeniden gönderilmedi.</param>
/// <param name="EnrollmentEnded">Yolculuk kaydı artık etkin değil (ileti bu yüzden gitmedi ya da alıcı abonelikten çıktığı için kayıt bu çağrıda çıkarıldı); kuralın kalan adımları atlanmalıdır.</param>
public sealed record JourneySendResult(string Outcome, string? Code, bool AlreadyHandled = false, bool EnrollmentEnded = false);

/// <summary>
/// Yolculuk iletisi gönderimi (ADR-0056 dilim 2a): bir yolculuk kaydının bir adımında içeriği hedefe gönderir. Kampanya gönderimiyle <b>aynı kurallar</b>
/// uygulanır (kural kopyalanmaz, aynı servisler kullanılır): alıcı çözümleyici, adres yeniden çözümü, iletişim izni ve engeli (konu dahil), günlük sıklık sınırı,
/// abonelikten çıkma bağlantısı ve sağlayıcı. Reddedilen ileti hata değildir: <see cref="JourneyMessageOutcomes.Skipped"/> ve neden koduyla günlüğe yazılır, yolculuk sürer.
/// Kayıt + adım başına tek satır vardır; satır gönderimden önce yazılır, çökme durumunda kör yeniden deneme olmaz (çift gönderim yerine belirsiz kalır).
/// </summary>
public interface IJourneySender
{
    Task<JourneySendResult> SendAsync(JourneySendRequest request, CancellationToken cancellationToken);
}

internal sealed class JourneySender(
    JourneyMessageStore store,
    IRecordPipeline pipeline,
    IRecordAccess access,
    IMarketingPolicyProvider policyProvider,
    IEnumerable<IMessageProvider> providers,
    IEnumerable<IRecipientResolver> resolvers,
    IEnumerable<IAddressLookup> lookups,
    ICommunicationPreferences preferences,
    IJourneyEnrollments enrollments,
    IFrequencyLimiter frequency,
    IUnsubscribeLinks unsubscribe,
    IJournalWriter journal,
    ITenantClock clock,
    TimeProvider time) : IJourneySender
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Alıcının pazarlama iletişiminden çıktığını gösteren karar kodları (ADR-0056 §6: abonelikten çıkma koşulsuz çıkıştır): izin geri alındı, konudan çıktı,
    /// "iletişim istemiyorum" ya da şikâyet engeli. Adres hatası ve sekme gibi teslim edilemezlik çıkış sayılmaz; yalnızca ileti atlanır.
    /// </summary>
    private static readonly HashSet<string> UnsubscribeCodes =
    [
        CommunicationCodes.Denied,
        CommunicationCodes.TopicOptOut,
        CommunicationCodes.Suppressed(SuppressionReason.OptOut),
        CommunicationCodes.Suppressed(SuppressionReason.Complaint),
    ];

    public async Task<JourneySendResult> SendAsync(JourneySendRequest request, CancellationToken cancellationToken)
    {
        if (await store.FindMessageAsync(request.EnrollmentId, request.StepKey, cancellationToken) is { } existing)
        {
            return new JourneySendResult(existing.Outcome, existing.Code, AlreadyHandled: true);
        }

        if (await store.FindEnrollmentAsync(request.EnrollmentId, cancellationToken) is not { } enrollment)
        {
            return new JourneySendResult(JourneyMessageOutcomes.Failed, "enrollment-not-found");
        }

        // Satır, gönderimden önce yazılır: aynı adım iki kez çağrılsa ya da çökse ikinci ileti gitmez.
        var message = new JourneyMessage
        {
            JourneyId = enrollment.JourneyId,
            EnrollmentId = enrollment.EnrollmentId,
            StepKey = request.StepKey,
            ContentId = request.ContentId,
            CreatedAt = time.GetUtcNow(),
            UpdatedAt = time.GetUtcNow(),
        };
        journal.Add(message);
        await journal.SaveAsync(cancellationToken);

        var (outcome, code, providerMessageId, channel) = await DecideAndSendAsync(request, enrollment, message, cancellationToken);
        message.Outcome = outcome;
        message.Code = code;
        message.ProviderMessageId = providerMessageId;
        message.Channel = channel;
        message.UpdatedAt = time.GetUtcNow();
        await journal.SaveAsync(cancellationToken);

        // Abonelikten çıkan alıcı yolculuktan çıkarılır (kalan adımlar atlanır); çıkarma yazılamazsa (yetki ya da çakışma) ileti yine de gönderilmemiş kalır.
        var ended = code == "journey-inactive";
        if (outcome == JourneyMessageOutcomes.Skipped && code is not null && UnsubscribeCodes.Contains(code))
        {
            ended = (await enrollments.ExitBySystemAsync(enrollment.EnrollmentId, EnrollmentReasons.Unsubscribed, cancellationToken)).IsSuccess;
        }

        return new JourneySendResult(outcome, code, EnrollmentEnded: ended);
    }

    private async Task<(string Outcome, string? Code, string? ProviderMessageId, string Channel)> DecideAndSendAsync(
        JourneySendRequest request, EnrollmentSendInfo enrollment, JourneyMessage message, CancellationToken cancellationToken)
    {
        if (enrollment.EnrollmentStatus != EnrollmentStatuses.Active || enrollment.JourneyStatus != JourneyStatuses.Active)
        {
            return Skipped("journey-inactive", string.Empty);
        }

        var content = await pipeline.GetAsync(new RecordRef(MarketingModule.Entities.CampaignContent, request.ContentId), cancellationToken);
        if (!content.IsSuccess)
        {
            return Failed("content-not-found", string.Empty);
        }

        var campaignId = Guid.Parse(content.Value!.Values["campaignId"]!.GetValue<string>());
        var campaign = await pipeline.GetAsync(new RecordRef(MarketingModule.Entities.Campaign, campaignId), cancellationToken);
        if (!campaign.IsSuccess)
        {
            return Failed("campaign-not-found", string.Empty);
        }

        var channel = Enum.Parse<CommunicationChannel>(campaign.Value!.Values["channel"]!.GetValue<string>());
        var topic = campaign.Value.Values["topic"]?.GetValue<string>();
        var channelName = channel.ToString();
        var subject = content.Value.Values["subject"]?.GetValue<string>();
        var body = content.Value.Values["body"]!.GetValue<string>();
        if (ContentRules.Validate(channel, subject, body) is { Count: > 0 })
        {
            return Failed("content-invalid", channelName);
        }

        var policy = await policyProvider.GetAsync(cancellationToken);
        var provider = policy.HasProvider ? providers.FirstOrDefault(p => p.Key == policy.Provider) : null;
        if (provider is null || !provider.Supports(channel))
        {
            return Failed("provider-missing", channelName);
        }

        if (!unsubscribe.IsAvailable)
        {
            return Failed("unsubscribe-link-unavailable", channelName);
        }

        // Alıcı: hedef türünü çözen çözümleyici; çağıranın çözümleyicinin kaynağını okuma izni yoksa çözülmez.
        var resolver = resolvers.FirstOrDefault(r => r.Supports(enrollment.Target));
        var lookup = lookups.FirstOrDefault(l => l.Supports(enrollment.Target));
        if (resolver is null || lookup is null)
        {
            return Failed("no-resolver", channelName);
        }

        if (!await access.CanAsync(resolver.ReadEntityKey, RecordOperation.Read, cancellationToken))
        {
            return Skipped("no-access", channelName);
        }

        var resolution = await resolver.ResolveAsync(new RecipientRequest(enrollment.Target, [enrollment.TargetId], channel, request.Rule), cancellationToken);
        if (resolution.Contacts.Count == 0)
        {
            return Skipped(resolution.Unresolved.FirstOrDefault()?.Code ?? RecipientCodes.NoMatchingContact, channelName);
        }

        var contact = resolution.Contacts[0];
        if (contact.IsShared)
        {
            return Skipped(RecipientCodes.SharedAddress, channelName);
        }

        // Adres gönderimden hemen önce yeniden çözülür (silinen ya da pasifleşen iletişim noktasına gönderilmez).
        var found = (await lookup.LookupAsync(enrollment.Target, channel, [new AddressLookup(contact.TargetId, contact.ContactPointId)], cancellationToken)).FirstOrDefault();
        var address = found is null ? null : CommunicationAddress.Normalize(channel, found.Address);
        if (address is null)
        {
            return Skipped("address-gone", channelName);
        }

        var decision = (await preferences.EvaluateManyAsync(channel, CommunicationPurpose.Marketing, [address], topic, cancellationToken))[address];
        if (!decision.IsAllowed)
        {
            return Skipped(decision.Code, channelName);
        }

        var today = (await clock.GetAsync(cancellationToken)).Today;
        if (policy.DailyLimit > 0 && !await frequency.TryReserveAsync(channel, address, today, policy.DailyLimit, cancellationToken))
        {
            return Skipped("frequency-limit", channelName);
        }

        var idempotencyKey = $"journey:{enrollment.EnrollmentId:N}:{request.StepKey}";
        var link = await unsubscribe.IssueAsync(channel, address, idempotencyKey, cancellationToken);
        if (link is null)
        {
            await ReleaseAsync(policy, channel, address, today, cancellationToken);
            return Failed("unsubscribe-link-unavailable", channelName);
        }

        ProviderResult result;
        try
        {
            var personalSubject = subject is null ? null : ContentTemplate.Render(subject, found!.Variables);
            var personalBody = ContentTemplate.Render(body, found!.Variables);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProviderTimeout);
            result = await provider.SendAsync(
                new MessageRequest(idempotencyKey, channel, address, personalSubject, ContentRules.WithUnsubscribeFooter(personalBody, link.PageUrl), link), timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Sağlayıcı iletiyi kabul etmiş olabilir: sonuç bilinmiyor, kör yeniden deneme yok, sıklık hakkı korunur.
            result = new ProviderResult(ProviderOutcome.Unknown, null, ex is OperationCanceledException ? "timeout" : "provider-exception");
        }

        switch (result.Outcome)
        {
            case ProviderOutcome.Accepted:
                return (JourneyMessageOutcomes.Accepted, null, result.ProviderMessageId, channelName);
            case ProviderOutcome.Unknown:
                return (JourneyMessageOutcomes.Unknown, result.ErrorCode ?? "unknown-result", result.ProviderMessageId, channelName);
            default:
                // Yolculukta geçici hata yeniden denenmez (adım tek seferliktir); iletiyi sağlayıcı kabul etmediği için hak geri verilir.
                await ReleaseAsync(policy, channel, address, today, cancellationToken);
                return (JourneyMessageOutcomes.Failed, result.ErrorCode ?? "provider-failed", null, channelName);
        }
    }

    private async Task ReleaseAsync(MarketingPolicy policy, CommunicationChannel channel, string address, DateOnly today, CancellationToken cancellationToken)
    {
        if (policy.DailyLimit > 0)
        {
            await frequency.ReleaseAsync(channel, address, today, cancellationToken);
        }
    }

    private static (string, string?, string?, string) Skipped(string code, string channel) => (JourneyMessageOutcomes.Skipped, code, null, channel);

    private static (string, string?, string?, string) Failed(string code, string channel) => (JourneyMessageOutcomes.Failed, code, null, channel);
}
