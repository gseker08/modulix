using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Authorization;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Ek ücret kuralı kaydı (ADR-0018 devamı, Dilim 2b): dört türün oluşturulması ve listelenmesi, türe göre alan doğrulaması, eşzamanlılık,
/// tenant izolasyonu ve fiyat izni. Kuralların hesabı birim testlerde (<c>SurchargeEngineTests</c>) kanıtlanır.
/// </summary>
public class SurchargeRuleApiTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Url = "/api/crm/surcharge-rules";
    private static readonly string[] ReaderPermissions = [PricesRead];
    private static readonly string[] CatalogOnly = [ProductsRead];

    [Fact]
    public async Task All_four_kinds_are_stored_listed_by_priority_and_round_trip_keys_and_conditions()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);

        var adr = await PostAsync<SurchargeRuleDto>(admin, new SaveSurchargeRuleRequest("ADR ek ücreti", "Fixed", "EUR", Priority: 20, Amount: 100m, Group: "ardiye",
            QuantityKeys: new JsonArray("beyan"), Conditions: new JsonArray(Condition("yuk", "Equals", "ADR")), Presentation: "IncludedInGroup"));
        await PostAsync<SurchargeRuleDto>(admin, new SaveSurchargeRuleRequest("ÖTV payı", "Percent", "EUR", Priority: 30, Percent: 0.5m, BaseKey: "teminat", Group: "ardiye"));
        await PostAsync<SurchargeRuleDto>(admin, new SaveSurchargeRuleRequest("Demuraj", "Duration", "EUR", Priority: 40, Percent: 10m, BaseKey: "line.parsiyel",
            DurationKey: "sure", FreeUnits: 7m, Group: "ardiye", Stackable: false));
        await PostAsync<SurchargeRuleDto>(admin, new SaveSurchargeRuleRequest("Asgari bedel", "Minimum", "EUR", Priority: 90, Amount: 125m, Group: "ardiye", Presentation: "BreakdownOnly"));

        var page = (await admin.GetFromJsonAsync<CrmPage<SurchargeRuleDto>>(Url))!;
        Assert.Equal(["ADR ek ücreti", "ÖTV payı", "Demuraj", "Asgari bedel"], page.Items.Select(r => r.Name));

        var stored = page.Items.Single(r => r.Name == "ADR ek ücreti");
        Assert.Equal(adr.Id, stored.Id);
        Assert.Equal("beyan", stored.QuantityKeys![0]!.GetValue<string>());
        Assert.Equal("ADR", stored.Conditions![0]!["value"]!.GetValue<string>());
        Assert.Equal("IncludedInGroup", stored.Presentation);
        var demurrage = page.Items.Single(r => r.Name == "Demuraj");
        Assert.Equal((7m, false), (demurrage.FreeUnits, demurrage.Stackable));

        // Güncelleme sürümle, pasife alma ve silme çalışır.
        var update = await admin.PutAsJsonAsync($"{Url}/{adr.Id}", Save(stored) with { IsActive = false });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var active = (await admin.GetFromJsonAsync<CrmPage<SurchargeRuleDto>>($"{Url}?active=true"))!;
        Assert.DoesNotContain(active.Items, r => r.Id == adr.Id);
        var updated = (await update.Content.ReadFromJsonAsync<SurchargeRuleDto>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{adr.Id}?version={updated.Version}")).StatusCode);
    }

    [Fact]
    public async Task Fields_are_validated_by_kind_and_stale_versions_are_rejected()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);

        // Tür başına zorunlu alanlar ve karşılıklı dışlama.
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("Tutarsız", "Fixed", "EUR"), "amount");
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("İkisi de", "Fixed", "EUR", Amount: 1m, Percent: 1m), "percent");
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("Tabansız", "Percent", "EUR", Percent: 1m), "baseKey");
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("Grupsuz", "Minimum", "EUR", Amount: 125m), "group");
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("Süresiz", "Duration", "EUR", Amount: 1m), "durationKey");

        // Geçersiz tür, görünüm, anahtar, yüzde ve liste öğeleri.
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("Tür", "Sihirli", "EUR", Amount: 1m), "kind");
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("Görünüm", "Fixed", "EUR", Amount: 1m, Presentation: "Gizli"), "presentation");
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("Anahtar", "Percent", "EUR", Percent: 1m, BaseKey: "boşluklu anahtar"), "baseKey");
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("Yüzde", "Percent", "EUR", Percent: 101m, BaseKey: "x"), "percent");
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("Tekrar", "Fixed", "EUR", Amount: 1m, QuantityKeys: new JsonArray("a", "a")), "quantityKeys");
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("İşleç", "Fixed", "EUR", Amount: 1m, Conditions: new JsonArray(Condition("a", "Benzer", "b"))), "conditions");
        await ExpectErrorsAsync(admin, new SaveSurchargeRuleRequest("Para birimi", "Fixed", "EURO", Amount: 1m), "currency");

        // Eşzamanlılık: eski sürümle yazma 409.
        var rule = await PostAsync<SurchargeRuleDto>(admin, new SaveSurchargeRuleRequest("Kural", "Fixed", "EUR", Amount: 10m));
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Url}/{rule.Id}", Save(rule) with { Amount = 20m })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"{Url}/{rule.Id}", Save(rule) with { Amount = 30m })).StatusCode);
    }

    [Fact]
    public async Task Rules_need_the_prices_permission_and_never_cross_tenants()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        using var reader = await LoginAsync(a.Reader);
        using var catalogOnly = await LoginAsync(a.CatalogOnly);

        var rule = await PostAsync<SurchargeRuleDto>(adminA, new SaveSurchargeRuleRequest("A kuralı", "Fixed", "EUR", Amount: 10m));

        // Yetki: fiyat okuma izni listeyi okur ama yazamaz; katalog izni olan fiyat kuralına hiç erişemez.
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"{Url}/{rule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Url, new SaveSurchargeRuleRequest("İzinsiz", "Fixed", "EUR", Amount: 1m))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"{Url}/{rule.Id}", Save(rule) with { Amount = 99m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await catalogOnly.GetAsync(Url)).StatusCode);

        // Tenant izolasyonu: B firması A'nın kuralını ne listede ne tekil okumada görür, değiştiremez.
        Assert.Empty((await adminB.GetFromJsonAsync<CrmPage<SurchargeRuleDto>>(Url))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync($"{Url}/{rule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.PutAsJsonAsync($"{Url}/{rule.Id}", Save(rule) with { Amount = 99m })).StatusCode);
        Assert.Equal(10m, (await adminA.GetFromJsonAsync<SurchargeRuleDto>($"{Url}/{rule.Id}"))!.Amount);
    }

    private static JsonObject Condition(string key, string op, string value) => new() { ["key"] = key, ["operator"] = op, ["value"] = value };

    private static SaveSurchargeRuleRequest Save(SurchargeRuleDto r) => new(
        r.Name, r.Kind, r.Currency, r.Description, r.Priority, r.IsActive, r.Amount, r.Percent, r.Group, r.BaseKey, r.DurationKey, r.FreeUnits,
        r.QuantityKeys, r.Conditions, r.Presentation, r.Stackable, r.Version);

    private static async Task ExpectErrorsAsync(HttpClient client, SaveSurchargeRuleRequest request, string field)
    {
        var response = await client.PostAsJsonAsync(Url, request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.True(problem.Errors.ContainsKey(field), $"'{field}' hatası bekleniyordu: {string.Join(", ", problem.Errors.Keys)}");
    }

    private static async Task<T> PostAsync<T>(HttpClient client, SaveSurchargeRuleRequest body)
    {
        var response = await client.PostAsJsonAsync(Url, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<(ModulixUser Admin, ModulixUser Reader, ModulixUser CatalogOnly)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        var catalogOnly = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, ReaderPermissions, Scope.All, reader);
        await factory.CreateRoleAsync(tenant.Id, CatalogOnly, Scope.All, catalogOnly);
        return (admin, reader, catalogOnly);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
