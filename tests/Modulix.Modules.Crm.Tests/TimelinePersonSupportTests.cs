using Modulix.Modules.Crm.Contracts.Timeline;
using Modulix.Modules.Crm.Records;

namespace Modulix.Modules.Crm.Tests;

/// <summary>Kişi çizelgesine hangi kaynakların girdiği kaynağın kendi bildiriminden gelir (ADR-0059); varsayılan "desteklemez"dir.</summary>
public class TimelinePersonSupportTests
{
    [Fact]
    public void A_source_that_does_not_override_the_declaration_does_not_support_persons()
    {
        ICustomerTimelineSource source = new PlainSource();
        Assert.False(source.SupportsPerson);
    }

    [Fact]
    public void Crm_sources_declare_person_support_through_their_contact_scope()
    {
        Assert.True(((ICustomerTimelineSource)new ActivityTimelineSource(null!, null!)).SupportsPerson);
        Assert.True(((ICustomerTimelineSource)new QuoteTimelineSource(null!, null!)).SupportsPerson);
        // Fırsat ve müşteri adayında kişi alanı yok (ADR-0059): alan eklenene kadar kişi çizelgesine girmezler.
        Assert.False(((ICustomerTimelineSource)new OpportunityTimelineSource(null!, null!)).SupportsPerson);
        Assert.False(((ICustomerTimelineSource)new LeadTimelineSource(null!)).SupportsPerson);
    }

    [Fact]
    public void A_query_is_a_person_query_when_a_contact_or_person_is_given()
    {
        var company = new CustomerTimelineQuery(Guid.NewGuid(), null, null, 10);
        Assert.False(company.IsPerson);
        Assert.True(company with { ContactId = Guid.NewGuid() } is { IsPerson: true });
        Assert.True(company with { PersonId = Guid.NewGuid() } is { IsPerson: true });
    }

    private sealed class PlainSource : ICustomerTimelineSource
    {
        public string Key => "test.plain";

        public Task<CustomerTimelineSlice?> ReadAsync(CustomerTimelineQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<CustomerTimelineSlice?>(new CustomerTimelineSlice([], false));
    }
}
