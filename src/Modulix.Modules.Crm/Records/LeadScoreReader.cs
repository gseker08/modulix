using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Leads;

namespace Modulix.Modules.Crm.Records;

/// <summary>
/// Toplu yeniden puanlamanın (ADR-0050, dilim 1b) salt okunur taraması: puanı etkin kurallarla tutmayan açık adayları bulur. Yazma kayıt
/// boru hattındadır (<see cref="LeadScoreJob"/>); okuyucu yalnızca kimlik döner.
/// </summary>
internal sealed class LeadScoreReader(IModuleData data)
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

        List<Guid> stale = [];
        await foreach (Lead lead in open.OrderBy(l => l.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (LeadScoring.Calculate(lead, rules) != lead.Score)
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
}
