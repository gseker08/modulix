using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>Atama penceresinin çalışma kopyası: seçim, kilitli satır, bağlı satırlar, gizli seçimlerin korunması ve süzgeçler.</summary>
public class PickSelectionTests
{
    private static readonly PickItem<string>[] Items =
    [
        new("crm.companies.read", "Firmalar: görüntüleme", Group: "CRM"),
        new("crm.companies.write", "Firmalar: düzenleme", Group: "CRM"),
        new("logistics.shipments.read", "Sevkiyatlar: görüntüleme", "yurt içi", "Lojistik"),
        new("core.users.manage", "Kullanıcı yönetimi", Group: "Yönetim", Locked: true),
    ];

    private static IEnumerable<string> Linked(string key, bool on) =>
        on && key.EndsWith(".write", StringComparison.Ordinal) ? [key[..^6] + ".read"]
        : !on && key.EndsWith(".read", StringComparison.Ordinal) ? [key[..^5] + ".write"]
        : [];

    [Fact]
    public void Toggling_changes_the_result_in_list_order()
    {
        PickSelection<string> selection = new(Items, ["logistics.shipments.read"]);

        selection.Toggle("crm.companies.read", true);

        Assert.Equal(["crm.companies.read", "logistics.shipments.read"], selection.Result);
        Assert.Equal(2, selection.Count);

        selection.Toggle("logistics.shipments.read", false);

        Assert.Equal(["crm.companies.read"], selection.Result);
    }

    [Fact]
    public void Initial_selection_is_not_modified()
    {
        HashSet<string> initial = ["crm.companies.read"];
        PickSelection<string> selection = new(Items, initial);

        selection.Toggle("crm.companies.read", false);

        Assert.Equal(["crm.companies.read"], initial);
        Assert.Empty(selection.Result);
    }

    [Fact]
    public void Locked_row_cannot_be_added_or_removed()
    {
        PickSelection<string> granted = new(Items, ["core.users.manage"]);
        granted.Toggle("core.users.manage", false);
        Assert.True(granted.IsSelected("core.users.manage"));

        PickSelection<string> missing = new(Items, []);
        missing.Toggle("core.users.manage", true);
        missing.SetAll(Items, true);
        Assert.False(missing.IsSelected("core.users.manage"));
    }

    [Fact]
    public void Unknown_key_is_ignored()
    {
        PickSelection<string> selection = new(Items, []);

        selection.Toggle("yok.boyle.izin", true);

        Assert.Empty(selection.Result);
    }

    [Fact]
    public void Linked_rows_follow_the_toggled_row()
    {
        PickSelection<string> selection = new(Items, [], Linked);

        selection.Toggle("crm.companies.write", true);
        Assert.True(selection.IsSelected("crm.companies.read"));

        selection.Toggle("crm.companies.read", false);
        Assert.False(selection.IsSelected("crm.companies.write"));
    }

    [Fact]
    public void Selections_outside_the_list_are_kept_in_the_result()
    {
        // Modülü kapalı izin listede görünmez; pencere onu silmemelidir.
        PickSelection<string> selection = new(Items, ["warehouse.stock.read", "crm.companies.read"]);

        selection.Toggle("crm.companies.read", false);

        Assert.Equal(["warehouse.stock.read"], selection.Result);
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void Visible_filters_by_search_group_and_selection()
    {
        PickSelection<string> selection = new(Items, ["crm.companies.read"]);

        Assert.Equal(2, selection.Visible("FIRMALAR").Count);
        Assert.Equal("logistics.shipments.read", Assert.Single(selection.Visible("yurt")).Key);
        Assert.Equal(2, selection.Visible(null, "CRM").Count);
        Assert.Equal("crm.companies.read", Assert.Single(selection.Visible(null, null, onlySelected: true)).Key);
        Assert.Empty(selection.Visible("sevkiyat", "CRM"));
    }

    [Fact]
    public void SetAll_selects_and_clears_the_given_rows()
    {
        PickSelection<string> selection = new(Items, []);
        IReadOnlyList<PickItem<string>> crm = selection.Visible(null, "CRM");

        selection.SetAll(crm, true);
        Assert.Equal(2, selection.Count);

        selection.SetAll(crm, false);
        Assert.Equal(0, selection.Count);
    }
}
