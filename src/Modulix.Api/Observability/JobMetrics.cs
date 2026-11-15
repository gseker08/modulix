using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Modulix.Core.Contracts.Diagnostics;
using Modulix.Infrastructure.Email;

namespace Modulix.Api.Observability;

/// <summary>
/// "Modulix.Jobs" ölçümleri (A2): arka plan işçilerinin turları (tamamlanan/başarısız), tur içinde başarısız olan tenant
/// sayısı, işçi başına son tamamlanan turun yaşı (işçi durduysa büyür; uyarı bunun üzerine kurulur) ve e-posta kuyruğu.
/// Etiketlerde yalnızca işçi adı ve sonuç vardır; tenant, kişi ya da kayıt yoktur (yüksek kardinalite ve kişisel veri olmasın).
/// </summary>
public sealed class JobMetrics : IJobMetrics
{
    public const string MeterName = "Modulix.Jobs";

    private readonly Counter<long> turns;
    private readonly Counter<long> tenantFailures;
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastCompleted = new(StringComparer.Ordinal);
    private readonly TimeProvider time;
    private EmailQueueSnapshot? emailQueue;

    public JobMetrics(IMeterFactory meters, TimeProvider time)
    {
        this.time = time;
        var meter = meters.Create(MeterName);
        turns = meter.CreateCounter<long>("modulix.jobs.turns", description: "Arka plan işçisi turları (sonuca göre).");
        tenantFailures = meter.CreateCounter<long>("modulix.jobs.tenant_failures", description: "Turda başarısız olan tenant çalıştırmaları.");
        meter.CreateObservableGauge(
            "modulix.jobs.last_completed_turn.age",
            ObserveTurnAges,
            "s",
            "İşçinin son tamamlanan turundan bu yana geçen süre.");
        meter.CreateObservableGauge(
            "modulix.email.pending", () => emailQueue?.Pending ?? 0, description: "Kuyrukta bekleyen e-posta sayısı.");
        meter.CreateObservableGauge(
            "modulix.email.pending.age", () => emailQueue?.OldestPendingAge.TotalSeconds ?? 0, "s", "En eski bekleyen e-postanın yaşı.");
    }

    /// <summary>İşçi başına son tamamlanan tur zamanı (gözlemleme ve testler için).</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> LastCompletedTurns => lastCompleted;

    public EmailQueueSnapshot? EmailQueue => emailQueue;

    /// <summary>Tur sonuna ulaşıldı (içindeki tek tek tenant hataları turu başarısız yapmaz).</summary>
    public void TurnCompleted(string job)
    {
        lastCompleted[job] = time.GetUtcNow();
        turns.Add(1, Tag("job", job), Tag("outcome", "completed"));
    }

    /// <summary>Tur başlatılamadı ya da yarıda kaldı (ör. veritabanına ulaşılamadı).</summary>
    public void TurnFailed(string job) => turns.Add(1, Tag("job", job), Tag("outcome", "failed"));

    /// <summary>Turda bir tenant'ın çalıştırması başarısız oldu; diğer tenant'lar etkilenmez.</summary>
    public void TenantFailed(string job) => tenantFailures.Add(1, Tag("job", job));

    public void ObserveEmailQueue(EmailQueueSnapshot snapshot) => emailQueue = snapshot;

    private IEnumerable<Measurement<double>> ObserveTurnAges()
    {
        var now = time.GetUtcNow();
        return lastCompleted.Select(pair => new Measurement<double>((now - pair.Value).TotalSeconds, Tag("job", pair.Key)));
    }

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);
}
