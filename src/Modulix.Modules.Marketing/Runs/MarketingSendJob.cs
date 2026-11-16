using System.Collections.Concurrent;
using Modulix.Core.Contracts.Modules;

namespace Modulix.Modules.Marketing.Runs;

/// <summary>
/// Pazarlama gönderim işi (ADR-0025 dilim 10, ADR-0046 kuyruk modu): host'taki tek zamanlayıcı, pazarlama modülü yazılabilir olan her tenant için
/// "çalışıyor" durumundaki kampanya çalıştırmalarının sıradaki satırlarını gönderir (<see cref="ICampaignRuns.ProcessRunningAsync"/>). İş "marketing.sender"
/// sistem kimliğiyle çalışır; çalıştırmayı başlatmaz ya da onaylamaz. Kapsamı, lisans kontrolünü, tenant başına kilidi (<c>modulix.marketing.send:&lt;tenant&gt;</c>;
/// satır talebi zaten kilitli ve atomiktir, kilit yalnızca gereksiz çakışmayı önler) ve kaydı zamanlayıcı yapar. Bir turda satır işlendiyse
/// <see cref="ModuleJobResult.HasMoreWork"/> bildirilir, sonraki tur kısa aralıkla başlar.
/// Ayarlar (<c>Jobs:marketing.send</c>, eski <c>MarketingSend</c> bölümü): <c>BatchSize</c> (100), <c>MaxBatchesPerRun</c> (5), <c>MaxRunsPerTenant</c> (5).
/// </summary>
internal sealed class MarketingSendJob(ICampaignRuns runs, MarketingSendErrors errors) : IModuleJob
{
    /// <summary>İşin anahtarı: yapılandırma, ölçüm adı ve kilit bu ada bağlıdır.</summary>
    public const string JobKey = "marketing.send";

    /// <summary>İşin sistem kimliği (<see cref="MarketingSenderAnchor"/> aboneliği).</summary>
    public const string Identity = "marketing.sender";

    public async Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken)
    {
        var processed = await runs.ProcessRunningAsync(
            context.PositiveInt("BatchSize", 100), context.PositiveInt("MaxBatchesPerRun", 5), context.PositiveInt("MaxRunsPerTenant", 5), cancellationToken);

        var messages = new List<string>();
        bool warning = false;
        foreach (var run in processed)
        {
            if (run.Error is { } error)
            {
                // Aynı hata her turda yinelenmez: yalnızca değişince yazılır.
                if (errors.Changed(run.RunId, error))
                {
                    warning = true;
                    messages.Add($"Çalıştırma {run.RunId} işlenemedi: {error}");
                }

                continue;
            }

            errors.Clear(run.RunId);
            if (run.Completed)
            {
                messages.Add($"Çalıştırma {run.RunId} tamamlandı ({run.Processed} satır işlendi).");
            }
        }

        return new ModuleJobResult(messages.Count > 0 ? string.Join(" ", messages) : null, processed)
        {
            IsWarning = warning,
            HasMoreWork = processed.Any(r => r.Processed > 0),
        };
    }
}

/// <summary>Çalıştırma başına son günlüğe yazılan hata (uygulama ömrü boyunca, tekil). Aynı hata her turda yinelenmesin diye tutulur.</summary>
internal sealed class MarketingSendErrors
{
    private readonly ConcurrentDictionary<Guid, string> last = new();

    /// <summary>Hata öncekinden farklıysa kaydeder ve <c>true</c> döner.</summary>
    public bool Changed(Guid runId, string error)
    {
        if (last.TryGetValue(runId, out var previous) && previous == error)
        {
            return false;
        }

        last[runId] = error;
        return true;
    }

    public void Clear(Guid runId) => last.TryRemove(runId, out _);
}
