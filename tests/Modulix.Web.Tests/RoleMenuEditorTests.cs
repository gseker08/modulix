using Modulix.Shared.Access;
using Modulix.Shared.Crm;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>
/// Rol menü düzeni düzenleyicisinin mantığı (E8 dilim 2b-2): düzenlenebilir öğe kümesi, taşıma, gizleme, sıra yeniden numaralama,
/// değişiklik durumu ve kayıt isteği. Gizlemek yetki değildir; bunlar yalnızca arayüz mantığı testleridir.
/// </summary>
public class RoleMenuEditorTests
{
    private static RoleMenuCatalogItem Item(string key, int order, string section = "crm/crm.sales") =>
        new(key, key, "list", section, order);

    private static RoleMenuEditor Editor(IEnumerable<RoleMenuItemDto>? saved = null) =>
        new(
            [Item("a", 10), Item("b", 20), Item("c", 30), Item("x", 10, "crm/crm.other")],
            saved ?? [],
            key => ("CRM", key));

    private static string[] Keys(RoleMenuEditor editor, string section) =>
        editor.Sections.Single(s => s.Key == section).Items.Select(i => i.Key).ToArray();

    [Fact]
    public void Kayitli_duzen_yoksa_katalog_sirasi_gecerlidir_ve_istek_bostur()
    {
        var editor = Editor();

        Assert.Equal(["a", "b", "c"], Keys(editor, "crm/crm.sales"));
        Assert.False(editor.IsDirty);
        Assert.False(editor.IsCustomized);
        Assert.Empty(editor.ToRequest().Items);
    }

    [Fact]
    public void Tasima_bolum_icinde_sirayi_degistirir_ve_sinirlarda_durur()
    {
        var editor = Editor();

        Assert.True(editor.Move("c", -1));
        Assert.Equal(["a", "c", "b"], Keys(editor, "crm/crm.sales"));
        Assert.False(editor.CanMove("a", -1));
        Assert.False(editor.Move("a", -1));
        Assert.False(editor.CanMove("b", 1));
        Assert.False(editor.CanMove("yok", 1));
        Assert.True(editor.IsDirty);
        Assert.Equal(["x"], Keys(editor, "crm/crm.other"));
    }

    [Fact]
    public void Siralanan_bolumde_butun_ogelere_yeniden_numaralanmis_sira_yazilir()
    {
        var editor = Editor();
        editor.Move("c", -1);

        var items = editor.ToRequest().Items;

        Assert.Equal(
            [new RoleMenuItemDto("a", 10, false), new RoleMenuItemDto("c", 20, false), new RoleMenuItemDto("b", 30, false)],
            items);
    }

    [Fact]
    public void Eski_siraya_donen_bolum_icin_sira_yazilmaz()
    {
        var editor = Editor();
        editor.Move("b", 1);
        editor.Move("c", 1);

        Assert.False(editor.IsDirty);
        Assert.Empty(editor.ToRequest().Items);
    }

    [Fact]
    public void Gizleme_siralamadan_bagimsiz_yazilir_ve_geri_alinabilir()
    {
        var editor = Editor();
        editor.SetHidden("b", true);

        Assert.True(editor.IsHidden("b"));
        Assert.True(editor.IsDirty);
        Assert.True(editor.IsCustomized);
        Assert.Equal([new RoleMenuItemDto("b", null, true)], editor.ToRequest().Items);

        editor.SetHidden("b", false);
        Assert.False(editor.IsDirty);
        Assert.Empty(editor.ToRequest().Items);
    }

    [Fact]
    public void Kayitli_duzen_yuklenir_ve_degisiklik_yok_durumundadir()
    {
        var editor = Editor([new("c", 5, false), new("a", 15, true), new("b", 25, false)]);

        Assert.Equal(["c", "a", "b"], Keys(editor, "crm/crm.sales"));
        Assert.True(editor.IsHidden("a"));
        Assert.False(editor.IsDirty);
        Assert.True(editor.IsCustomized);
    }

    [Fact]
    public void Siralari_bos_kayitli_satirlar_katalog_sirasini_korur()
    {
        var editor = Editor([new("b", null, true)]);

        Assert.Equal(["a", "b", "c"], Keys(editor, "crm/crm.sales"));
        Assert.True(editor.IsHidden("b"));
    }

    [Fact]
    public void Katalogda_olmayan_kayitli_satir_korunur()
    {
        var editor = Editor([new("kapali-modul.ekran", 10, true)]);
        editor.SetHidden("a", true);

        Assert.Contains(new RoleMenuItemDto("kapali-modul.ekran", 10, true), editor.ToRequest().Items);
        Assert.Contains(new RoleMenuItemDto("a", null, true), editor.ToRequest().Items);
    }

    [Fact]
    public void Varsayilana_don_her_seyi_temizler_ve_bos_istek_uretir()
    {
        var editor = Editor([new("kapali-modul.ekran", 10, false), new("c", 5, true), new("a", 15, false), new("b", 25, false)]);

        editor.Reset();

        Assert.Equal(["a", "b", "c"], Keys(editor, "crm/crm.sales"));
        Assert.False(editor.IsHidden("c"));
        Assert.False(editor.IsCustomized);
        Assert.Empty(editor.ToRequest().Items);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void Kayit_sonrasi_yukleme_degisiklik_durumunu_sifirlar()
    {
        var editor = Editor();
        editor.Move("a", 1);
        var saved = editor.ToRequest().Items;

        editor.Load(saved);

        Assert.False(editor.IsDirty);
        Assert.Equal(["b", "a", "c"], Keys(editor, "crm/crm.sales"));
    }

    [Fact]
    public void Sira_sunucunun_kabul_ettigi_araligi_asmaz()
    {
        var items = Enumerable.Range(1, 200).Select(i => Item($"k{i}", i)).ToList();
        var editor = new RoleMenuEditor(items, [], key => ("CRM", key));
        editor.Move("k200", -1);

        Assert.All(editor.ToRequest().Items, i => Assert.InRange(i.Order!.Value, 0, 10_000));
        Assert.All(editor.ToRequest().Items, i => Assert.Matches("^[a-z0-9]+([.-][a-z0-9]+)*$", i.ItemKey));
    }

    [Fact]
    public void Katalog_ogeleri_sabit_alt_bolumu_platformu_ve_kapali_modulu_icermez()
    {
        var items = RoleMenuEditor.CatalogItems(module => module == CrmAccess.ModuleKey);

        Assert.Contains(items, i => i.Key == "crm.companies");
        Assert.DoesNotContain(items, i => i.Key == "crm.settings");
        Assert.DoesNotContain(items, i => i.Key.StartsWith("platform.", StringComparison.Ordinal));
        Assert.DoesNotContain(items, i => i.Key.StartsWith("hr.", StringComparison.Ordinal));
        Assert.DoesNotContain(items, i => i.Key == "crm.quote-approvals");
        Assert.Equal(items.Count, items.Select(i => i.Key).Distinct().Count());
    }

    [Fact]
    public void Ev_sahibi_acikken_barindirilan_ekran_ev_sahibinin_bolumunde_duzenlenir()
    {
        var withHost = RoleMenuEditor.CatalogItems(_ => true);
        var withoutHost = RoleMenuEditor.CatalogItems(module => module != CrmAccess.ModuleKey);

        Assert.StartsWith("crm/", withHost.Single(i => i.Key == "orders.list").SectionKey, StringComparison.Ordinal);
        Assert.StartsWith("orders/", withoutHost.Single(i => i.Key == "orders.list").SectionKey, StringComparison.Ordinal);
        Assert.DoesNotContain(withHost, i => i.Key == "orders.settings");
    }

    [Fact]
    public void Katalogdan_uretilen_anahtarlar_sunucu_bicimine_uyar()
    {
        Assert.All(
            RoleMenuEditor.CatalogItems(_ => true),
            i => Assert.Matches("^[a-z0-9]+([.-][a-z0-9]+)*$", i.Key));
    }
}
