using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Numbering;
using Modulix.Core.Modules;
using Modulix.Infrastructure.Identity;
using Modulix.Infrastructure.Persistence;
using Modulix.Modules.Logistics;
using Modulix.Shared.Auth;
using Modulix.Shared.Numbering;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0049 dilim 2a: firma belge serisini yapılandırır (önek, basamak, sıfırlama) ve sayacı ileri alır. Mutlu yol, doğrulama ve çakışma,
/// tenant izolasyonu (başka firmanın yapılandırması ve sayacı etkilenmez), yetki (izinsiz kullanıcı göremez ve değiştiremez).
/// </summary>
public class NumberSeriesAdminTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Series = "/api/numbering/series";
    private static readonly string Trip = LogisticsModule.TripNumberSeries;

    [Fact]
    public async Task Company_configures_the_series_and_advances_the_counter()
    {
        var (tenantId, admin) = await SetUpAsync();
        using var client = await LoginAsync(admin);

        var listed = await TripSeriesAsync(client);
        Assert.Equal(("SF", 5, "Yearly", false, 0L, 0L), (listed.Prefix, listed.Digits, listed.Reset, listed.IsCustomized, listed.LastValue, listed.Version));

        var configured = await ReadAsync<NumberSeriesDto>(
            await client.PutAsJsonAsync($"{Series}/{Trip}", new ConfigureNumberSeriesRequest("sev", 4, "Never", 0)), HttpStatusCode.OK);
        Assert.Equal(("SEV", 4, "Never", true, "SEV-0001", 1L), (configured.Prefix, configured.Digits, configured.Reset, configured.IsCustomized, configured.NextNumber, configured.Version));
        Assert.Equal("SF", configured.DefaultPrefix);

        Assert.Equal("SEV-0001", await NextAsync(tenantId));

        var advanced = await ReadAsync<NumberSeriesDto>(
            await client.PostAsJsonAsync($"{Series}/{Trip}/advance", new AdvanceNumberSeriesRequest(50, configured.Version)), HttpStatusCode.OK);
        Assert.Equal(("SEV-0050", 49L, 2L), (advanced.NextNumber, advanced.LastValue, advanced.Version));
        Assert.Equal("SEV-0050", await NextAsync(tenantId));

        using var scope = factory.OpenScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<ModulixDbContext>();
        Assert.Equal(2, await db.AuditLog.CountAsync(a => a.EntityKey == CoreModule.AuditedEntityKeys.NumberSeries));
    }

    [Fact]
    public async Task Invalid_values_stale_versions_and_backward_moves_are_rejected()
    {
        var (tenantId, admin) = await SetUpAsync();
        using var client = await LoginAsync(admin);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"{Series}/{Trip}", new ConfigureNumberSeriesRequest("S-F", 5, "Yearly", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"{Series}/{Trip}", new ConfigureNumberSeriesRequest("SF", 2, "Yearly", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"{Series}/{Trip}", new ConfigureNumberSeriesRequest("SF", 5, "Daily", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{Series}/logistics.unknown", new ConfigureNumberSeriesRequest("SF", 5, "Yearly", 0))).StatusCode);

        // Bu dönemde numara verilmeden ileri alınamaz (modülün eski numaralarından devam eden tohum ilk numarada sorulur).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"{Series}/{Trip}/advance", new AdvanceNumberSeriesRequest(10, 0))).StatusCode);

        await NextAsync(tenantId);
        await NextAsync(tenantId);
        var current = await TripSeriesAsync(client);
        Assert.Equal(2, current.LastValue);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"{Series}/{Trip}/advance", new AdvanceNumberSeriesRequest(3, current.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"{Series}/{Trip}/advance", new AdvanceNumberSeriesRequest(1, current.Version))).StatusCode);

        var configured = await ReadAsync<NumberSeriesDto>(
            await client.PutAsJsonAsync($"{Series}/{Trip}", new ConfigureNumberSeriesRequest("SEV", 5, "Yearly", current.Version)), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{Series}/{Trip}", new ConfigureNumberSeriesRequest("SF", 5, "Yearly", current.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{Series}/{Trip}/advance", new AdvanceNumberSeriesRequest(100, current.Version))).StatusCode);

        // Önek değişse de sayaç kaldığı yerden sürer: aynı sıra iki kez verilmez.
        Assert.Matches(@"^SEV-\d{4}-00003$", await NextAsync(tenantId));
        Assert.Equal(1, configured.Version);
    }

    [Fact]
    public async Task Another_tenants_configuration_and_counter_are_untouched()
    {
        var (tenantA, adminA) = await SetUpAsync();
        var (tenantB, adminB) = await SetUpAsync();
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);

        await ReadAsync<NumberSeriesDto>(await clientA.PutAsJsonAsync($"{Series}/{Trip}", new ConfigureNumberSeriesRequest("SEV", 4, "Never", 0)), HttpStatusCode.OK);
        await NextAsync(tenantA);
        var a = await TripSeriesAsync(clientA);
        await ReadAsync<NumberSeriesDto>(await clientA.PostAsJsonAsync($"{Series}/{Trip}/advance", new AdvanceNumberSeriesRequest(500, a.Version)), HttpStatusCode.OK);

        var b = await TripSeriesAsync(clientB);
        Assert.Equal(("SF", false, 0L, 0L), (b.Prefix, b.IsCustomized, b.LastValue, b.Version));
        Assert.Matches(@"^SF-\d{4}-00001$", await NextAsync(tenantB));

        // B, A'nın sürümüyle A'nın ayarına ulaşamaz: kendi satırı yok, sürüm uyuşmaz.
        Assert.Equal(HttpStatusCode.Conflict, (await clientB.PutAsJsonAsync($"{Series}/{Trip}", new ConfigureNumberSeriesRequest("X", 4, "Never", 2))).StatusCode);
        Assert.Equal("SEV-0500", await NextAsync(tenantA));
    }

    [Fact]
    public async Task Users_without_the_numbering_permission_cannot_read_or_change_series()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, LogisticsModule.ModuleKey, enabled: true);
        var user = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CoreModule.Permissions.ManageSettings, LogisticsModule.Permissions.TripsWrite], user);
        using var client = await LoginAsync(user);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Series)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"{Series}/{Trip}", new ConfigureNumberSeriesRequest("SEV", 4, "Never", 0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Series}/{Trip}/advance", new AdvanceNumberSeriesRequest(10, 0))).StatusCode);
        Assert.Matches(@"^SF-\d{4}-00001$", await NextAsync(tenant.Id));
    }

    [Fact]
    public async Task Series_of_a_module_that_is_not_enabled_are_hidden_and_cannot_be_changed()
    {
        // Lojistik açık, CRM kapalı: teklif serisi listelenmez ve yapılandırılamaz (ADR-0049 dilim 3a).
        var (_, admin) = await SetUpAsync();
        using var client = await LoginAsync(admin);
        var quote = Modulix.Modules.Crm.CrmModule.QuoteNumberSeries;

        var listed = await ReadAsync<List<NumberSeriesDto>>(await client.GetAsync(Series), HttpStatusCode.OK);
        Assert.Contains(listed, s => s.Key == Trip);
        Assert.DoesNotContain(listed, s => s.Key == quote);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{Series}/{quote}", new ConfigureNumberSeriesRequest("TKL", 5, "Yearly", 0))).StatusCode);
    }

    private async Task<(Guid TenantId, ModulixUser Admin)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, LogisticsModule.ModuleKey, enabled: true);
        await factory.SetModuleAsync(tenant.Id, Modulix.Modules.Crm.CrmModule.ModuleKey, enabled: false);
        var admin = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, admin);
        return (tenant.Id, admin);
    }

    private async Task<string> NextAsync(Guid tenantId)
    {
        using var scope = factory.OpenScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IDocumentNumbers>().NextAsync(Trip);
    }

    private static async Task<NumberSeriesDto> TripSeriesAsync(HttpClient client) =>
        Assert.Single(await ReadAsync<List<NumberSeriesDto>>(await client.GetAsync(Series), HttpStatusCode.OK), s => s.Key == Trip);

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
