using Modulix.Core.Contracts.Modules;

namespace Modulix.Modules.Crm.Tests;

/// <summary>Teklif ve teklif satırı özel alana açıktır, teklif formu metadata'dan gelir (ADR-0021; veritabanı gerektirmez).</summary>
public class QuoteCustomizationTests
{
    [Fact]
    public void Quote_and_quote_line_are_customizable_entities()
    {
        var entities = ((IModule)new CrmModule()).CustomizableEntities.Select(e => e.EntityKey).ToList();

        Assert.Contains(CrmModule.Entities.Quote, entities);
        Assert.Contains(CrmModule.Entities.QuoteLine, entities);
        Assert.Equal(entities.Count, entities.Distinct().Count());
    }

    [Fact]
    public void Quote_default_form_keeps_identity_fields_fixed_and_leaves_a_place_for_custom_fields()
    {
        var form = CrmForms.All.Single(f => f.EntityKey == CrmModule.Entities.Quote);

        Assert.Contains(form.Sections, s => s.Key == FormDefinition.ExtrasSection);
        foreach (var key in new[] { "title", "companyId", "currency", "priceIncludesTax" })
        {
            Assert.True(form.Fields.Single(f => f.Key == key).Fixed, $"{key} gizlenemez olmalı.");
        }

        // Sunucudan gelen toplamlar ve durumlar formda yer almaz: istemci bunları yazamaz.
        string[] serverOwned = ["status", "listTotal", "netTotal", "grossTotal", "approvalStatus", "quoteNumber"];
        Assert.DoesNotContain(form.Fields, f => serverOwned.Contains(f.Key));
        Assert.Equal(form.Fields.Count, form.Fields.Select(f => f.Key).Distinct().Count());
        Assert.All(form.Fields, f => Assert.Contains(form.Sections, s => s.Key == f.Section));
        Assert.All(form.Fields, f => Assert.Contains(f.Span, FormSpans.Allowed));
    }
}
