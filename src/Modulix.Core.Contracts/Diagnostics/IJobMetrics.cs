namespace Modulix.Core.Contracts.Diagnostics;

/// <summary>
/// Arka plan işçisi turlarının ölçümü (gözlemlenebilirlik, A2). İşçiler modüle taşındığında da (ADR-0046) aynı arayüzle
/// ölçülür; uygulaması host'tadır ("Modulix.Jobs" ölçümleri). İş adı sabit ve küçük bir kümedir (ör. <c>orders.jobs</c>);
/// tenant, kişi ya da kayıt kimliği verilmez.
/// </summary>
public interface IJobMetrics
{
    /// <summary>Tur sonuna ulaşıldı (içindeki tek tek tenant hataları turu başarısız yapmaz).</summary>
    void TurnCompleted(string job);

    /// <summary>Tur başlatılamadı ya da yarıda kaldı (ör. veritabanına ulaşılamadı).</summary>
    void TurnFailed(string job);

    /// <summary>Turda bir tenant'ın çalıştırması başarısız oldu; diğer tenant'lar etkilenmez.</summary>
    void TenantFailed(string job);
}
