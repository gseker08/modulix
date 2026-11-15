using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Numbering;
using Modulix.Infrastructure.Identity;
using Modulix.Infrastructure.Persistence;
using Modulix.Modules.Logistics;
using Modulix.Shared.Auth;
using Modulix.Shared.Records;
using static Modulix.Modules.Logistics.LogisticsModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0049 dilim 1: ortak belge numaralama, ilk kullanıcısı sefer numarası. Mutlu yol (ardışık numara, aynı anda açılan seferler ayrı numara alır,
/// tohum yalnızca sayaç açılırken kullanılır), hata (beyansız seri, tenant bağlamı yok, reddedilen kayıt numara harcamaz), tenant izolasyonu
/// (her tenant kendi sayacını görür ve 1'den başlar) ve yetki (yazma izni olmayan kullanıcı numara tüketemez).
/// </summary>
public class DocumentNumberTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Trips = "/api/records/logistics.trip";

    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Trips_get_consecutive_numbers_and_concurrent_trips_never_share_one()
    {
        var s = await SetUpAsync();
        using var manager = await LoginAsync(s.Manager);

        var first = Sequence(await CreateTripAsync(manager));
        var second = Sequence(await CreateTripAsync(manager));
        Assert.Equal((1, 2), (first, second));

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => manager.PostAsJsonAsync(Trips, TripBody())));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var numbers = new List<long>();
        foreach (var response in responses)
        {
            numbers.Add(Sequence((await response.Content.ReadFromJsonAsync<RecordResponse>())!));
        }

        Assert.Equal(Enumerable.Range(3, 8).Select(i => (long)i), numbers.Order());
    }

    [Fact]
    public async Task The_seed_is_used_only_when_the_counter_is_opened()
    {
        var s = await SetUpAsync();
        using var scope = factory.OpenScope(s.TenantId);
        var numbers = scope.ServiceProvider.GetRequiredService<IDocumentNumbers>();

        // Seri gelmeden önce 41 sefer açılmış gibi: sayaç 41'den devam eder; sonraki çağrıda tohum artık sorulmaz.
        var first = await numbers.NextAsync(LogisticsModule.TripNumberSeries, (_, _) => Task.FromResult(41L));
        var second = await numbers.NextAsync(LogisticsModule.TripNumberSeries, (_, _) => throw new InvalidOperationException("tohum yeniden soruldu"));

        Assert.Matches(@"^SF-\d{4}-00042$", first);
        Assert.Matches(@"^SF-\d{4}-00043$", second);
    }

    [Fact]
    public async Task Undeclared_series_and_missing_tenant_are_rejected_and_a_rejected_record_spends_no_number()
    {
        var s = await SetUpAsync();
        using (var scope = factory.OpenScope(s.TenantId))
        {
            var numbers = scope.ServiceProvider.GetRequiredService<IDocumentNumbers>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => numbers.NextAsync("logistics.unknown"));
        }

        using (var scope = factory.OpenScope(null))
        {
            var numbers = scope.ServiceProvider.GetRequiredService<IDocumentNumbers>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => numbers.NextAsync(LogisticsModule.TripNumberSeries));
        }

        using var manager = await LoginAsync(s.Manager);
        Assert.Equal(1, Sequence(await CreateTripAsync(manager)));
        var invalid = await manager.PostAsJsonAsync(Trips, new RecordWriteRequest(new JsonObject { ["plannedStart"] = Today, ["vehicleId"] = Guid.NewGuid().ToString() }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal(2, Sequence(await CreateTripAsync(manager)));
    }

    [Fact]
    public async Task Each_tenant_has_its_own_counter_and_cannot_see_anothers()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var managerA = await LoginAsync(a.Manager);
        using var managerB = await LoginAsync(b.Manager);

        await CreateTripAsync(managerA);
        await CreateTripAsync(managerA);
        Assert.Equal(3, Sequence(await CreateTripAsync(managerA)));
        Assert.Equal(1, Sequence(await CreateTripAsync(managerB)));

        using var scopeB = factory.OpenScope(b.TenantId);
        var counters = await scopeB.ServiceProvider.GetRequiredService<ModulixDbContext>().NumberCounters.AsNoTracking().ToListAsync();
        var counter = Assert.Single(counters);
        Assert.Equal((b.TenantId, LogisticsModule.TripNumberSeries, 1L), (counter.TenantId, counter.SeriesKey, counter.LastValue));
    }

    [Fact]
    public async Task A_user_without_write_permission_cannot_consume_a_number()
    {
        var s = await SetUpAsync();
        using var manager = await LoginAsync(s.Manager);
        using var reader = await LoginAsync(s.Reader);

        Assert.Equal(1, Sequence(await CreateTripAsync(manager)));
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Trips, TripBody())).StatusCode);
        Assert.Equal(2, Sequence(await CreateTripAsync(manager)));
    }

    // --- yardımcılar ---

    private sealed record Setup(Guid TenantId, ModulixUser Manager, ModulixUser Reader);

    private async Task<Setup> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "logistics", enabled: true);
        var manager = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [TripsRead, TripsWrite], Scope.All, manager);
        await factory.CreateRoleAsync(tenant.Id, [TripsRead], Scope.All, reader);
        return new Setup(tenant.Id, manager, reader);
    }

    private static RecordWriteRequest TripBody() => new(new JsonObject { ["plannedStart"] = Today });

    private static async Task<RecordResponse> CreateTripAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Trips, TripBody());
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    /// <summary>Sefer numarasının sıra kısmı (SF-yıl-sıra).</summary>
    private static long Sequence(RecordResponse trip)
    {
        var number = trip.Values["number"]!.GetValue<string>();
        Assert.Matches(@"^SF-\d{4}-\d{5}$", number);
        return long.Parse(number[^5..], CultureInfo.InvariantCulture);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
