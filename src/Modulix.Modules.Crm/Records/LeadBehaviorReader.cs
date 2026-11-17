using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Crm.Domain;
using Modulix.Modules.Crm.Leads;

namespace Modulix.Modules.Crm.Records;

/// <summary>
/// Müşteri adayı davranış sayaçlarını (<see cref="LeadBehavior"/>, ADR-0050 dilim 2-3) CRM'in kendi tablolarından türetir. Salt okunurdur; puan bir
/// sunucu kararı olduğu için okuma çağıranın kapsamına bağlı değildir, ama özel aktiviteler sayılmaz (puan varlıklarını sezdirmesin).
/// Hareketsiz gün sayısı <paramref name="now"/> anına göre hesaplanır.
/// </summary>
internal sealed class LeadBehaviorReader(IModuleData data, DateTimeOffset now)
{
    private const string MeetingType = "Meeting";

    /// <summary>Tek adayın sayaçları (aday kaydedilirken).</summary>
    public async Task<LeadBehavior> ForLeadAsync(Lead lead, CancellationToken cancellationToken)
    {
        List<ContactRow> activities = await Rows(CompletedActivities().Where(a => a.LeadId == lead.Id)).ToListAsync(cancellationToken);
        int repeats = await data.Set<Lead>().AsNoTracking()
            .Where(l => l.Id != lead.Id
                && ((lead.NormalizedEmail != null && l.NormalizedEmail == lead.NormalizedEmail)
                    || (lead.NormalizedPhone != null && l.NormalizedPhone == lead.NormalizedPhone)))
            .CountAsync(cancellationToken);
        return Count(lead, activities, repeats);
    }

    /// <summary>
    /// Tenant'ın bütün adaylarının sayaçları (toplu yeniden puanlama). Aday kimliği listesiyle süzülmez (SQL parametre sınırı); tamamlanmış aday
    /// aktiviteleri ve adayların iletişim anahtarları birer sorguyla okunur, sayım bellekte yapılır. Dönen fonksiyon her aday için sayaçları verir;
    /// aktivitesi ve tekrar başvurusu olmayan adayın hareketsiz gün sayısı oluşturulduğu andan sayılır.
    /// </summary>
    public async Task<Func<Lead, LeadBehavior>> ForTenantAsync(CancellationToken cancellationToken)
    {
        List<ContactRow> activities = await Rows(CompletedActivities()).ToListAsync(cancellationToken);
        var keys = await data.Set<Lead>().AsNoTracking()
            .Where(l => l.NormalizedEmail != null || l.NormalizedPhone != null)
            .Select(l => new { l.Id, l.NormalizedEmail, l.NormalizedPhone })
            .ToListAsync(cancellationToken);

        ILookup<string, Guid> byEmail = keys.Where(k => k.NormalizedEmail != null).ToLookup(k => k.NormalizedEmail!, k => k.Id);
        ILookup<string, Guid> byPhone = keys.Where(k => k.NormalizedPhone != null).ToLookup(k => k.NormalizedPhone!, k => k.Id);
        Dictionary<Guid, int> repeats = [];
        foreach (var key in keys)
        {
            IEnumerable<Guid> same = (key.NormalizedEmail is { } email ? byEmail[email] : [])
                .Concat(key.NormalizedPhone is { } phone ? byPhone[phone] : []);
            int count = same.Where(id => id != key.Id).Distinct().Count();
            if (count > 0)
            {
                repeats[key.Id] = count;
            }
        }

        ILookup<Guid, ContactRow> byLead = activities.ToLookup(a => a.LeadId);
        return lead => Count(lead, [.. byLead[lead.Id]], repeats.GetValueOrDefault(lead.Id));
    }

    private IQueryable<Activity> CompletedActivities() =>
        data.Set<Activity>().AsNoTracking().Where(a => a.LeadId != null && a.CompletedAt != null && !a.IsPrivate);

    // Projeksiyon en sonda: süzgeçler entity üzerinde kalsın ki SQL'e çevrilsin.
    private static IQueryable<ContactRow> Rows(IQueryable<Activity> source) =>
        source.Select(a => new ContactRow(a.LeadId!.Value, a.Type, a.Outcome, a.CompletedAt!.Value));

    private LeadBehavior Count(Lead lead, IReadOnlyCollection<ContactRow> activities, int repeats)
    {
        List<ContactRow> contacts = [.. activities.Where(a => LeadRules.CountsAsContact(a.Type, a.Outcome, completed: true))];
        return new LeadBehavior(contacts.Count, contacts.Count(a => a.Type == MeetingType), repeats, InactiveDays(lead, contacts));
    }

    /// <summary>
    /// Son gerçekleşen temastan (yoksa adayın oluşturulmasından; geriye tarihli temas oluşturmadan önce sayılmaz) bu yana geçen tam gün sayısı.
    /// </summary>
    private int InactiveDays(Lead lead, List<ContactRow> contacts)
    {
        DateTimeOffset last = contacts.Count == 0 ? lead.CreatedAt : contacts.Max(a => a.CompletedAt);
        if (last < lead.CreatedAt)
        {
            last = lead.CreatedAt;
        }

        return Math.Max(0, (int)Math.Floor((now - last).TotalDays));
    }

    private sealed record ContactRow(Guid LeadId, string Type, string? Outcome, DateTimeOffset CompletedAt);
}
