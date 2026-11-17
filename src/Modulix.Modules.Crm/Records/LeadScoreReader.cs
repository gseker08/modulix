using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Leads;
using LeadScoreExplanationDto = Modulix.Shared.Crm.LeadScoreExplanationDto;
using LeadScoreReasonDto = Modulix.Shared.Crm.LeadScoreReasonDto;

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

        (List<LeadScoreRule> rules, LeadBehavior? behavior) = await InputsAsync(lead, cancellationToken);
        return LeadScoring.Calculate(lead, rules, behavior) != lead.Score;
    }

    /// <summary>
    /// Puan açıklaması (dilim 4): kayıttaki puan, bugünkü kurallarla hesaplanan puan ve katkı veren kurallar. Aday bulunamazsa null. Görünürlük
    /// çağıranın sorunudur: uç nokta önce adayı kayıt boru hattından okur (izin ve kapsam), sonra bunu çağırır.
    /// </summary>
    public async Task<LeadScoreExplanationDto?> ExplainAsync(Guid leadId, CancellationToken cancellationToken)
    {
        Lead? lead = await data.Set<Lead>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        (List<LeadScoreRule> rules, LeadBehavior? behavior) = await InputsAsync(lead, cancellationToken);
        List<LeadScoreReasonDto> reasons =
            [.. LeadScoring.Explain(lead, rules, behavior).Select(r => new LeadScoreReasonDto(r.Id, r.Name, r.Field, r.Operator, r.Value, r.Points))];
        return new LeadScoreExplanationDto(lead.Score, LeadScoring.Calculate(lead, rules, behavior), reasons);
    }

    /// <summary>Etkin kurallar ve (bir kural kullanıyorsa) adayın davranış sayaçları; aday kaydedilirken yapılan hesabın girdileri.</summary>
    private async Task<(List<LeadScoreRule> Rules, LeadBehavior? Behavior)> InputsAsync(Lead lead, CancellationToken cancellationToken)
    {
        List<LeadScoreRule> rules = await data.Set<LeadScoreRule>().AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken);
        LeadBehavior? behavior = LeadScoring.NeedsBehavior(rules) ? await new LeadBehaviorReader(data, time.GetUtcNow()).ForLeadAsync(lead, cancellationToken) : null;
        return (rules, behavior);
    }

    /// <summary>Aktivitenin bugün bağlı olduğu aday (silinmişse ya da adaya bağlı değilse null).</summary>
    public Task<Guid?> LeadOfActivityAsync(Guid activityId, CancellationToken cancellationToken) =>
        data.Set<Activity>().AsNoTracking().Where(a => a.Id == activityId).Select(a => a.LeadId).SingleOrDefaultAsync(cancellationToken);
}
