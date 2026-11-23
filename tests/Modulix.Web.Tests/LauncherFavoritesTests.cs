using Modulix.Web.Services;

namespace Modulix.Web.Tests;

/// <summary>
/// Paneldeki "Sık Kullanılanlar": tarayıcıda saklanan kimlik listesi. Kişisel kolaylıktır, yetki değildir; saklanan kimlik
/// yalnızca kullanıcı o uygulamayı hâlâ görebiliyorsa gösterilir.
/// </summary>
public class LauncherFavoritesTests
{
    private static readonly IReadOnlyList<AppDefinition> Roots = AppCatalog.All.Where(a => a.ParentId is null).ToList();

    [Fact]
    public void Toggle_adds_then_removes_and_round_trips_through_storage()
    {
        var ids = LauncherFavorites.Toggle([], AppCatalog.Crm);
        ids = LauncherFavorites.Toggle(ids, AppCatalog.Support);

        Assert.Equal([AppCatalog.Crm, AppCatalog.Support], LauncherFavorites.Parse(LauncherFavorites.Serialize(ids)));
        Assert.Equal([AppCatalog.Support], LauncherFavorites.Toggle(ids, AppCatalog.Crm));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bozuk")]
    [InlineData("{\"a\":1}")]
    [InlineData("null")]
    public void Unreadable_stored_value_is_an_empty_list(string? stored)
    {
        Assert.Empty(LauncherFavorites.Parse(stored));
    }

    [Fact]
    public void Stored_list_is_deduplicated_and_capped()
    {
        var many = Enumerable.Range(0, LauncherFavorites.Max + 5).Select(i => $"app{i}").ToList();

        Assert.Equal(LauncherFavorites.Max, LauncherFavorites.Parse(LauncherFavorites.Serialize(many)).Count);
        Assert.Equal(["crm"], LauncherFavorites.Parse("[\"crm\",\"crm\",\"\"]"));
        var full = many.Take(LauncherFavorites.Max).ToList();
        Assert.Equal(full, LauncherFavorites.Toggle(full, "fazla"));
    }

    [Fact]
    public void Only_apps_the_user_can_still_see_are_shown_in_starred_order()
    {
        // Yetki: yıldızlanmış ama artık görünmeyen (izni ya da modülü kalkmış) uygulama listelenmez.
        var visible = Roots.Where(r => r.Id is AppCatalog.Crm or AppCatalog.Support).ToList();

        var shown = LauncherFavorites.Visible([AppCatalog.Support, AppCatalog.Admin, "silinmis", AppCatalog.Crm], visible);

        Assert.Equal([AppCatalog.Support, AppCatalog.Crm], shown.Select(a => a.Id));
    }

    [Fact]
    public void Storage_key_is_separate_per_user_and_tenant()
    {
        // Tenant izolasyonu: A firmasında yıldızlananlar B firmasında (ya da başka kullanıcıda) okunmaz.
        var user = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        Assert.NotEqual(LauncherFavorites.StorageKey(user, tenantA), LauncherFavorites.StorageKey(user, tenantB));
        Assert.NotEqual(LauncherFavorites.StorageKey(user, tenantA), LauncherFavorites.StorageKey(Guid.NewGuid(), tenantA));
        Assert.Equal(LauncherFavorites.StorageKey(user, tenantA), LauncherFavorites.StorageKey(user, tenantA));
    }
}
