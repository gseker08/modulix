using Modulix.Core.Contracts.Credentials;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Contracts.SharedRecords;
using Modulix.Core.Modules;

namespace Modulix.Core.Tests.Modules;

/// <summary>
/// Seçim listesine modül katkısı (H2): Core'un ortak listeleri sektörden bağımsız hazır değerleri taşır; sektöre özel değerleri sahibi modül ekler.
/// Katkı listenin tanımına modül sırasıyla eklenir, liste Core'un kalır; geçersiz katkı katalog kurulurken reddedilir.
/// </summary>
public class ChoiceValueContributionTests
{
    [Fact]
    public void Core_lists_carry_no_sector_vocabulary_on_their_own()
    {
        var catalog = new ModuleCatalog([new CoreModule()]);

        var credentialTypes = catalog.FindChoiceList(CredentialEntities.TypeList)!.Value.List;
        Assert.DoesNotContain(credentialTypes.Values, v => v.Category is CredentialCategories.Vehicle or CredentialCategories.Driver);
        Assert.Equal(CredentialCategories.All, credentialTypes.Categories.Select(c => c.Key));
        Assert.DoesNotContain(catalog.FindChoiceList(Place.KindList)!.Value.List.Values, v => v.Key == "BondedWarehouse");
    }

    [Fact]
    public void A_contribution_is_appended_to_the_owning_modules_list_in_module_order()
    {
        var catalog = new ModuleCatalog(
        [
            new CoreModule(),
            new ContributingModule("fleet", new(CredentialEntities.TypeList, [new("Inspection", "Araç muayenesi", CredentialCategories.Vehicle)])),
            new ContributingModule("logistics", new(CredentialEntities.TypeList, [new("Src", "SRC belgesi", CredentialCategories.Driver)])),
        ]);

        var (moduleKey, list) = catalog.FindChoiceList(CredentialEntities.TypeList)!.Value;
        Assert.Equal(CoreModule.ModuleKey, moduleKey);
        Assert.Equal(["Inspection", "Src"], list.Values.TakeLast(2).Select(v => v.Key));
        Assert.Same(list, catalog.ChoiceLists.Single(l => l.List.Key == CredentialEntities.TypeList).List);
        Assert.Same(list, catalog.ChoiceListsOf(CredentialEntities.Credential).Single(l => l.Key == CredentialEntities.TypeList));
    }

    [Theory]
    [InlineData("core.unknown-list", "Inspection", CredentialCategories.Vehicle)] // tanımsız liste
    [InlineData(CredentialEntities.TypeList, "PowerOfAttorney", CredentialCategories.Party)] // anahtar listede zaten var
    [InlineData(CredentialEntities.TypeList, "Inspection", "spaceship")] // listede olmayan kategori
    [InlineData(CredentialEntities.TypeList, "Inspection", null)] // kategorili listede kategorisiz değer
    [InlineData(CredentialEntities.TypeList, "bad key!", CredentialCategories.Vehicle)] // geçersiz anahtar
    public void An_invalid_contribution_is_rejected(string listKey, string valueKey, string? category)
    {
        Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
            [new CoreModule(), new ContributingModule("fleet", new(listKey, [new(valueKey, "Değer", category)]))]));
    }

    [Fact]
    public void A_module_cannot_contribute_to_its_own_list_or_send_an_empty_contribution()
    {
        Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
            [new CoreModule(), new ContributingModule("fleet", new(CredentialEntities.TypeList, []))]));
        var ownList = new ChoiceListDefinition("fleet.kind", "Tür", "fleet.vehicle", "kindKey", Required: false, [], [new("Truck", "Tır")]);
        Assert.Throws<InvalidOperationException>(() => new ModuleCatalog(
            [new CoreModule(), new ContributingModule("fleet", new("fleet.kind", [new("Van", "Kamyonet")])) { Lists = [ownList] }]));
    }

    private sealed class ContributingModule(string key, ChoiceValueContribution contribution) : IModule
    {
        public string Key => key;

        public string DisplayName => key;

        public IReadOnlyList<PermissionDefinition> Permissions => [];

        public IReadOnlyList<ChoiceListDefinition> Lists { get; init; } = [];

        public IReadOnlyList<ChoiceListDefinition> ChoiceLists => Lists;

        public IReadOnlyList<ChoiceValueContribution> ChoiceValueContributions => [contribution];
    }
}
