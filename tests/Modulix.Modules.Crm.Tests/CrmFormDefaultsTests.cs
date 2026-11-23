namespace Modulix.Modules.Crm.Tests;

/// <summary>Arayüz kuralı (CLAUDE.md → Arayüz tasarım kuralları): her kayıt formu sekmelidir; varsayılan düzenler çok sekmelidir (veritabanı gerektirmez).</summary>
public class CrmFormDefaultsTests
{
    [Fact]
    public void Every_default_form_has_several_tabs_and_each_tab_holds_a_section()
    {
        Assert.NotEmpty(CrmForms.All);
        foreach (var form in CrmForms.All)
        {
            Assert.True(form.Tabs.Count >= 2, $"{form.EntityKey}: varsayılan form sekmeli olmalı (en az 2 sekme).");
            Assert.Equal("genel", form.Tabs[0].Key);
            Assert.Equal(form.Tabs.Count, form.Tabs.Select(t => t.Key).Distinct().Count());
            foreach (var tab in form.Tabs)
            {
                Assert.True(form.Sections.Any(s => s.Tab == tab.Key), $"{form.EntityKey}: '{tab.Key}' sekmesinde bölüm yok.");
            }
        }
    }

    [Fact]
    public void Company_contact_tab_holds_the_multi_record_lists_next_to_quick_entry_fields()
    {
        // Adres tipine göre çoklu adres, telefon/e-posta ve lokasyon listeleri İletişim sekmesindedir (ADR-0012);
        // tekil adres/telefon/e-posta/şehir yalnızca yeni firmada hızlı giriş olarak çizilir.
        var company = CrmForms.All.Single(f => f.EntityKey == CrmModule.Entities.Company);
        var contactSections = company.Sections.Where(s => s.Tab == "iletisim").Select(s => s.Key).ToHashSet();
        foreach (var key in new[] { "addresses", "contactPoints", "locations", "address", "phone", "email", "city" })
        {
            var field = Assert.Single(company.Fields, f => f.Key == key);
            Assert.Contains(field.Section, contactSections);
        }
    }

    [Fact]
    public void Required_and_fixed_identity_fields_stay_on_the_first_tab()
    {
        // Zorunlu kimlik alanları ilk sekmede olmalı: kullanıcı kayda başlarken eksik alanı gizli sekmede aramaz.
        foreach (var form in CrmForms.All)
        {
            var firstTabSections = form.Sections.Where(s => s.Tab == form.Tabs[0].Key).Select(s => s.Key).ToHashSet();
            foreach (var field in form.Fields.Where(f => f is { Required: true, Fixed: true }))
            {
                Assert.True(firstTabSections.Contains(field.Section), $"{form.EntityKey}: zorunlu '{field.Key}' alanı ilk sekmede değil.");
            }
        }
    }
}
