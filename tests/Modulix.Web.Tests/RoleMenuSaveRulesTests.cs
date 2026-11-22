using Modulix.Shared.Access;
using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>
/// E8 dilim 2b: rol menü düzeni kaydı bütün düzeni değiştirdiği için ekranda listelenmeyen öğenin kayıtlı satırı korunmalıdır;
/// aksi halde modülü geçici kapalı bir öğenin düzeni bir sonraki kayıtta silinir.
/// </summary>
public class RoleMenuSaveRulesTests
{
    private static readonly RoleMenuItemDto Shown = new("crm.companies", 20, false);
    private static readonly RoleMenuItemDto Hidden = new("crm.leads", null, true);
    private static readonly RoleMenuItemDto ClosedModule = new("logistics.shipments", 10, true);

    [Fact]
    public void Rows_of_items_missing_from_the_screen_are_preserved()
    {
        IReadOnlyList<RoleMenuItemDto> preserved =
            RoleMenuSaveRules.Preserved([Shown, Hidden, ClosedModule], ["crm.companies", "crm.leads"]);

        Assert.Equal([ClosedModule], preserved);
    }

    [Fact]
    public void Rows_of_items_on_the_screen_are_not_preserved_because_the_screen_writes_them()
    {
        IReadOnlyList<RoleMenuItemDto> preserved = RoleMenuSaveRules.Preserved([Shown, Hidden], ["crm.companies", "crm.leads"]);

        Assert.Empty(preserved);
    }

    [Fact]
    public void An_empty_saved_layout_preserves_nothing()
    {
        Assert.Empty(RoleMenuSaveRules.Preserved([], ["crm.companies"]));
    }

    [Fact]
    public void Preserved_rows_are_matched_by_exact_key()
    {
        IReadOnlyList<RoleMenuItemDto> preserved = RoleMenuSaveRules.Preserved([new RoleMenuItemDto("crm.companies", 10, false)], ["crm.Companies"]);

        Assert.Single(preserved);
    }
}
