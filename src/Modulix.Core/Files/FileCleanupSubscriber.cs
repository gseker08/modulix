using Modulix.Core.Contracts.Events;
using Modulix.Core.Contracts.Files;
using Modulix.Core.Contracts.Records;

namespace Modulix.Core.Files;

/// <summary>
/// Silinen kaydın dosyaları (ADR-0040 dilim 3): kayıt silindi olayında kaydın bütün dosyalarının üst verisi silinir ve denetim kaydına yazılır. Her
/// kayıt türü kendiliğinden kapsanır; modül ek iş yapmaz. İçeriği yetim içerik temizliği siler. Teslim tekrar edilirse silinecek dosya kalmamıştır.
/// </summary>
public sealed class FileCleanupSubscriber(IRecordFileCleanup cleanup) : IEventSubscriber
{
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken) =>
        cleanup.RemoveForRecordAsync(new RecordRef(envelope.EntityKey, envelope.RecordId), cancellationToken);
}
