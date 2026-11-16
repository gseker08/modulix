using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Leads;

namespace Modulix.Modules.Crm.Records;

/// <summary>
/// Toplu yeniden puanlamanın (ADR-0050, dilim 1b) salt okunur taraması: puanı etkin kurallarla tutmayan açık adayları bulur. Yazma kayıt
/// boru hattındadır (<see cref="LeadScoreJob"/>); okuyucu yalnızca kimlik döner.
/// </summary>
internal sealed class LeadScoreReader(IModuleData data, TimeProvider time)
{
    /// <summary>Puanı eskimiş açık adaylar (kimlik sırasıyla), en çok <paramref name="limit"/> tane.</summary>
    public async Task<List<Guid>> StaleOpenLeadsAsync(int limit, CancellationToken cancellationToken)
    {
        List<LeadScoreRule> rules = await data.Set<LeadScoreRule>().AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken);
        IQueryable<Lead> open = data.Set<Lead>().AsNoTracking().Where(l => LeadStatus.Open.Contains(l.Status));
        if (rules.Count == 0)
        {
            // Kural yoksa doğru puan sıfırdır: yalnızca sıfır olmayanlar taranır (kural kullanmayan tenant'ta tur neredeyse bedavadır).
            open = open.Where(l => l.Score != 0);
        }

        // Davranış sayaçları (dilim 2-3) yalnızca bir etkin kural kullanıyorsa, tenant için bir kez okunur.
        Func<Lead, LeadBehavior?> behaviors = _ => null;
        if (LeadScoring.NeedsBehavior(rules))
        {
            behaviors = await new LeadBehaviorReader(data, time.GetUtcNow()).ForTenantAsync(cancellationToken);
        }

        List<Guid> stale = [];
        await foreach (Lead lead in open.OrderBy(l => l.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (LeadScoring.Calculate(lead, rules, behaviors(lead)) != lead.Score)
            {
                stale.Add(lead.Id);
                if (stale.Count == limit)
                {
                    break;
                }
            }
        }

        return stale;
    }

    /// <summary>
    /// Tek adayın puanı eskimiş mi (dilim 2: aktivite değişince)? Açık değilse ya da yoksa false. Hesap aday kaydedilirken yapılanın aynısıdır
    /// (<c>LeadRecordStore</c>); yalnızca değişecekse yazılsın diye önceden bakılır (boş güncelleme de denetim satırı ve olay üretir).
    /// </summary>
    public async Task<bool> IsStaleAsync(Guid leadId, CancellationToken cancellationToken)
    {
        Lead? lead = await data.Set<Lead>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null || !LeadStatus.IsOpen(lead.Status))
        {
            return false;
        }

        List<LeadScoreRule> rules = await data.Set<LeadScoreRule>().AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken);
        LeadBehavior? behavior = LeadScoring.NeedsBehavior(rules) ? await new LeadBehaviorReader(data, time.GetUtcNow()).ForLeadAsync(lead, cancellationToken) : null;
        return LeadScoring.Calculate(lead, rules, behavior) != lead.Score;
    }

    /// <summary>Aktivitenin bugün bağlı olduğu aday (silinmişse ya da adaya bağlı değilse null).</summary>
    public Task<Guid?> LeadOfActivityAsync(Guid activityId, CancellationToken cancellationToken) =>
        data.Set<Activity>().AsNoTracking().Where(a => a.Id == activityId).Select(a => a.LeadId).SingleOrDefaultAsync(cancellationToken);
}
