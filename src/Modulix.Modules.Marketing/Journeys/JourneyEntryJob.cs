using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Events;
using Modulix.Core.Contracts.Modules;
using Modulix.Modules.Marketing.Records;
using Modulix.Modules.Marketing.Runs;

namespace Modulix.Modules.Marketing.Journeys;

/// <summary>
/// Yolculuk giriş işi (ADR-0056 dilim 1b-2, ADR-0046): her tenant için etkin yolculukların giriş segmentini yeniler ve yeni girenleri kaydeder
/// (<see cref="IJourneyEntry"/>). Segment, yolculuğu etkinleştirenin okuma izinleriyle değerlendirilir (<see cref="IGrantedExecution"/>); iş kimliğinin
/// kendisi başka modülün alanlarını okuyamaz. Ayarlar (<c>Jobs:marketing.journey-entry</c>): <c>MaxEntriesPerJourney</c> (500, tur başına).
/// </summary>
internal sealed class JourneyEntryJob(JourneyEntryStore store, IGrantedExecution granted, MarketingSendErrors errors) : IModuleJob
{
    /// <summary>İşin anahtarı: yapılandırma, ölçüm adı ve kilit bu ada bağlıdır.</summary>
    public const string JobKey = "marketing.journey-entry";

    /// <summary>İşin sistem kimliği (<see cref="JourneyEntryAnchor"/> aboneliği).</summary>
    public const string Identity = "marketing.journeys";

    public async Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken)
    {
        var max = context.PositiveInt("MaxEntriesPerJourney", 500);
        var outcomes = new List<JourneyEntryOutcome>();
        var messages = new List<string>();
        var warning = false;
        foreach (var journey in await store.ListActiveAsync(cancellationToken))
        {
            var outcome = await granted.RunAsync(
                journey.Permissions, (services, token) => services.GetRequiredService<IJourneyEntry>().EnterAsync(journey.Id, max, token), cancellationToken);
            outcomes.Add(outcome);
            if (outcome.Error is { } error)
            {
                // Aynı hata her turda yinelenmez: yalnızca değişince yazılır.
                if (errors.Changed(journey.Id, error))
                {
                    warning = true;
                    messages.Add($"Yolculuk '{journey.Code}' girişi işlenemedi: {error}");
                }

                continue;
            }

            errors.Clear(journey.Id);
            if (outcome.Entered > 0)
            {
                messages.Add($"Yolculuk '{journey.Code}': {outcome.Entered} yeni giriş.");
            }
        }

        return new ModuleJobResult(messages.Count > 0 ? string.Join(" ", messages) : null, outcomes)
        {
            IsWarning = warning,
            HasMoreWork = outcomes.Any(o => o.Remaining > 0),
        };
    }
}

/// <summary>"marketing.journeys" kimliğinin izin çapası (giriş işinin izinlerini tanımlar); olay işlemez.</summary>
internal sealed class JourneyEntryAnchor : IEventSubscriber
{
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
}
