using System.Text.Json.Nodes;
using Modulix.Core.Contracts.Events;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Crm.Records;

namespace Modulix.Modules.Crm.Leads;

/// <summary>
/// Toplu yeniden puanlama (ADR-0050, dilim 1b). Kural eklenince, değişince ya da silinince mevcut adayların puanı eskir; bu iş her turda açık
/// adayları etkin kurallarla yeniden hesaplar (<see cref="LeadScoreReader"/>) ve puanı tutmayanları kayıt boru hattından, sistem adına günceller
/// (boş değerli güncelleme <c>LeadRecordStore.AppliedAsync</c> içindeki puan hesabını çalıştırır). Böylece puan değişikliği denetim kaydına ve
/// <c>RecordUpdated</c> olayına her zamanki gibi girer. Ayrı bir "yeniden hesapla" işareti tutulmaz: puanı tutan aday dokunulmadan geçilir, iki
/// kez çalışan tur ikinci kez bir şey yazmaz. Bir turda en çok <see cref="BatchSize"/> aday güncellenir; kalanlar sonraki turlara kalır.
/// Kapanmış (elenmiş ya da dönüştürülmüş) adayın puanı donar: o anki puan tarihî bilgidir.
/// </summary>
internal sealed class LeadScoreJob(LeadScoreReader reader, IRecordPipeline pipeline) : IModuleJob
{
    /// <summary>İşin anahtarı: yapılandırma (<c>Jobs:crm.lead-scoring</c>), ölçüm adı ve kilit bu ada bağlıdır.</summary>
    public const string JobKey = "crm.lead-scoring";

    /// <summary>İşin sistem kimliği (<see cref="LeadScoreJobAnchor"/> aboneliği).</summary>
    public const string Identity = "crm.lead-scoring";

    /// <summary>Bir turda güncellenen en çok aday sayısı (tur tek işlemde kaydedilir; işlem kısa kalsın).</summary>
    public const int BatchSize = 200;

    public async Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken)
    {
        List<Guid> stale = await reader.StaleOpenLeadsAsync(BatchSize, cancellationToken);
        int updated = 0;
        int failed = 0;
        foreach (Guid id in stale)
        {
            RecordResult<RecordData> result = await pipeline.UpdateAsync(new RecordCommand(CrmModule.Entities.Lead, id, new JsonObject()), cancellationToken);
            if (result.IsSuccess)
            {
                updated++;
            }
            else
            {
                failed++;
            }
        }

        LeadScoreJobRun run = new(stale.Count, updated, failed);
        string? summary = stale.Count > 0 ? $"{updated} müşteri adayının puanı yeniden hesaplandı, {failed} aday güncellenemedi." : null;
        return new ModuleJobResult(summary, run);
    }
}

/// <summary>Tur özeti: puanı tutmayan (bu turda ele alınan) aday sayısı, güncellenen ve güncellenemeyen.</summary>
public sealed record LeadScoreJobRun(int Stale, int Updated, int Failed);

/// <summary>"crm.lead-scoring" kimliğinin izin çapası: olay işlemez, yalnızca zamanlanmış işin izinlerini tanımlar.</summary>
internal sealed class LeadScoreJobAnchor : IEventSubscriber
{
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
}
