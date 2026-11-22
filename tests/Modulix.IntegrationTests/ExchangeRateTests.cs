using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Currency;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Records;
using static Modulix.Core.Modules.CoreModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0049 dilim 5a: firmanın döviz kurları Core'da (<c>core.exchange-rate</c>). Mutlu yol (ekleme, süzgeç, düzeltme, silme, sözleşmeden doğrudan ve ters
/// kur okuma; CRM modülü olmayan firma), doğrulama (sıfır kur, aynı para birimi, geçersiz kod ve kaynak, aynı çift ve gün), tenant izolasyonu (başka
/// tenant'ın kuru görünmez ve değişmez; aynı çift ve gün başka tenant'ta serbest) ve yetki (izinsiz üye okur ama yazamaz). CRM fiyat hesabı aynı kuru
/// kullanır (<see cref="PricingTests"/>).
/// </summary>
public class ExchangeRateTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Rates = "/api/records/" + CurrencyEntities.ExchangeRate;

    [Fact]
    public async Task Rates_are_added_filtered_corrected_removed_and_read_through_the_contract_without_the_crm_module()
    {
        // Tenant'ın hiçbir modül hakkı yok (CRM dahil): kur Core ana verisidir.
        var s = await SetUpAsync();
        using var keeper = await LoginAsync(s.Keeper);

        var older = await PostAsync(keeper, Rate("USD", "TRY", 30m, "2027-01-01"));
        var newer = await PostAsync(keeper, Rate("USD", "TRY", 32.5m, "2027-02-01", note: " Şubat kuru "));
        await PostAsync(keeper, Rate("EUR", "TRY", 35m, "2027-01-15"));
        Assert.Equal(("Manual", "Şubat kuru"), (newer.Values["source"]!.GetValue<string>(), newer.Values["note"]!.GetValue<string>()));

        Assert.Equal(2, (await ListAsync(keeper, "fromCurrency=usd")).Count);
        Assert.Equal(["EUR"], (await ListAsync(keeper, "fromCurrency=EUR&toCurrency=try")).Select(r => r.Values["fromCurrency"]!.GetValue<string>()));
        Assert.Equal(["2027-02-01", "2027-01-01"], (await ListAsync(keeper, "fromCurrency=USD&sort=-validOn")).Select(r => r.Values["validOn"]!.GetValue<string>()));

        var corrected = await keeper.PutAsJsonAsync($"{Rates}/{older.Id}", new RecordWriteRequest(new JsonObject { ["rate"] = 31m }, older.Version));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);

        using (var scope = factory.OpenScope(s.TenantId))
        {
            var reader = scope.ServiceProvider.GetRequiredService<IExchangeRateReader>();

            // Doğrudan kur: istenen güne kadarki en yeni kayıt.
            var jan = await reader.FindAsync("USD", "TRY", new DateOnly(2027, 1, 20), CancellationToken.None);
            Assert.Equal((31m, new DateOnly(2027, 1, 1), false), (jan!.Rate, jan.RateDate, jan.Inverted));
            var feb = await reader.FindAsync("USD", "TRY", new DateOnly(2027, 2, 10), CancellationToken.None);
            Assert.Equal((32.5m, new DateOnly(2027, 2, 1)), (feb!.Rate, feb.RateDate));

            // Ters kur: TRY → EUR kayıtlı değil, EUR → TRY'nin tersi alınır; çapraz kur (USD → EUR) türetilmez; kayıttan önceki gün "yok" döner.
            var inverse = await reader.FindAsync("TRY", "EUR", new DateOnly(2027, 3, 1), CancellationToken.None);
            Assert.True(inverse!.Inverted);
            Assert.Equal(1m / 35m, inverse.Rate);
            Assert.Null(await reader.FindAsync("USD", "EUR", new DateOnly(2027, 3, 1), CancellationToken.None));
            Assert.Null(await reader.FindAsync("USD", "TRY", new DateOnly(2026, 12, 31), CancellationToken.None));
            Assert.Equal(3, (await reader.ListAsync(CancellationToken.None)).Count);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await keeper.DeleteAsync($"{Rates}/{newer.Id}?version={newer.Version}")).StatusCode);
        using var after = factory.OpenScope(s.TenantId);
        var left = await after.ServiceProvider.GetRequiredService<IExchangeRateReader>().FindAsync("USD", "TRY", new DateOnly(2027, 2, 10), CancellationToken.None);
        Assert.Equal(31m, left!.Rate);
    }

    [Fact]
    public async Task Zero_same_currency_malformed_and_duplicate_rates_are_rejected()
    {
        var s = await SetUpAsync();
        using var keeper = await LoginAsync(s.Keeper);
        var first = await PostAsync(keeper, Rate("USD", "TRY", 30m, "2027-05-19"));

        Assert.True((await ProblemAsync(keeper, Rate("USD", "TRY", 0m, "2027-05-20"))).Errors.ContainsKey("rate"));
        Assert.True((await ProblemAsync(keeper, Rate("USD", "TRY", -1m, "2027-05-20"))).Errors.ContainsKey("rate"));
        Assert.True((await ProblemAsync(keeper, Rate("TRY", "TRY", 1m, "2027-05-20"))).Errors.ContainsKey("toCurrency"));
        Assert.True((await ProblemAsync(keeper, Rate("US", "TRY", 30m, "2027-05-20"))).Errors.ContainsKey("fromCurrency"));
        Assert.True((await ProblemAsync(keeper, Rate("USD", "TR1", 30m, "2027-05-20"))).Errors.ContainsKey("toCurrency"));
        Assert.True((await ProblemAsync(keeper, Rate("USD", "TRY", 30m, "20.05.2027"))).Errors.ContainsKey("validOn"));
        var badSource = Rate("USD", "TRY", 30m, "2027-05-20");
        badSource["source"] = "Tahmin";
        badSource["color"] = "red";
        var errors = (await ProblemAsync(keeper, badSource)).Errors;
        Assert.True(errors.ContainsKey("source") && errors.ContainsKey("color"));
        Assert.True((await ProblemAsync(keeper, new JsonObject { ["fromCurrency"] = "USD" })).Errors.ContainsKey("toCurrency"));

        var duplicate = await ProblemAsync(keeper, Rate("USD", "TRY", 41m, "2027-05-19"));
        Assert.Equal(["Bu para birimi çifti için o güne ait kur zaten var."], duplicate.Errors["validOn"]);

        // Düzeltme de aynı kurallara tabidir: ikinci kuru ilkinin gününe taşımak çakışır, eski sürümle yazmak çakışma (409) verir.
        var second = await PostAsync(keeper, Rate("USD", "TRY", 31m, "2027-05-21"));
        var clash = await keeper.PutAsJsonAsync($"{Rates}/{second.Id}", new RecordWriteRequest(new JsonObject { ["validOn"] = "2027-05-19" }, second.Version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, clash.StatusCode);
        var stale = await keeper.PutAsJsonAsync($"{Rates}/{first.Id}", new RecordWriteRequest(new JsonObject { ["rate"] = 32m }, first.Version + 5));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await keeper.GetAsync($"{Rates}?fromCurrency=EURO")).StatusCode);
    }

    [Fact]
    public async Task Another_tenants_rates_are_invisible_and_untouchable_and_the_same_pair_and_day_is_free_there()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var keeperA = await LoginAsync(a.Keeper);
        using var keeperB = await LoginAsync(b.Keeper);
        var rateA = await PostAsync(keeperA, Rate("USD", "TRY", 30m, "2027-04-23"));

        Assert.DoesNotContain(await ListAsync(keeperB, null), r => r.Id == rateA.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await keeperB.GetAsync($"{Rates}/{rateA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await keeperB.PutAsJsonAsync($"{Rates}/{rateA.Id}",
            new RecordWriteRequest(new JsonObject { ["rate"] = 1m }, rateA.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await keeperB.DeleteAsync($"{Rates}/{rateA.Id}?version={rateA.Version}")).StatusCode);
        await PostAsync(keeperB, Rate("USD", "TRY", 99m, "2027-04-23"));

        using var scopeB = factory.OpenScope(b.TenantId);
        var reader = scopeB.ServiceProvider.GetRequiredService<IExchangeRateReader>();
        Assert.Equal(99m, (await reader.FindAsync("USD", "TRY", new DateOnly(2027, 5, 1), CancellationToken.None))!.Rate);
        Assert.Single(await reader.ListAsync(CancellationToken.None));

        // A'nın kuru B'nin okuyucusunda yok; A hâlâ kendi kurunu görüyor.
        using var scopeA = factory.OpenScope(a.TenantId);
        Assert.Equal(30m, (await scopeA.ServiceProvider.GetRequiredService<IExchangeRateReader>()
            .FindAsync("USD", "TRY", new DateOnly(2027, 5, 1), CancellationToken.None))!.Rate);
    }

    [Fact]
    public async Task Members_read_the_rates_but_only_the_exchange_rate_permission_writes_them()
    {
        var s = await SetUpAsync();
        using var keeper = await LoginAsync(s.Keeper);
        using var member = await LoginAsync(s.Member);
        var rate = await PostAsync(keeper, Rate("USD", "TRY", 30m, "2027-08-30"));

        Assert.Contains(await ListAsync(member, null), r => r.Id == rate.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(Rates, new RecordWriteRequest(Rate("EUR", "TRY", 35m, "2027-08-31")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync($"{Rates}/{rate.Id}",
            new RecordWriteRequest(new JsonObject { ["rate"] = 1m }, rate.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync($"{Rates}/{rate.Id}?version={rate.Version}")).StatusCode);
    }

    // --- yardımcılar ---

    private sealed record Setup(Guid TenantId, ModulixUser Keeper, ModulixUser Member);

    /// <summary>Kurları yöneten rol ("keeper") ve rolü olmayan üye. Tenant'ın hiçbir modül hakkı yoktur.</summary>
    private async Task<Setup> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        var keeper = await factory.CreateUserAsync(tenant);
        var member = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ManageExchangeRates], Scope.All, keeper);
        return new Setup(tenant.Id, keeper, member);
    }

    private static JsonObject Rate(string from, string to, decimal rate, string validOn, string? note = null) => new()
    {
        ["fromCurrency"] = from,
        ["toCurrency"] = to,
        ["rate"] = rate,
        ["validOn"] = validOn,
        ["note"] = note,
    };

    private static async Task<RecordResponse> PostAsync(HttpClient client, JsonObject values)
    {
        var response = await client.PostAsJsonAsync(Rates, new RecordWriteRequest(values));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    private static async Task<ValidationProblemDetails> ProblemAsync(HttpClient client, JsonObject values)
    {
        var response = await client.PostAsJsonAsync(Rates, new RecordWriteRequest(values));
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
    }

    private static async Task<IReadOnlyList<RecordResponse>> ListAsync(HttpClient client, string? query) =>
        (await client.GetFromJsonAsync<RecordPageResponse>(query is null ? Rates : $"{Rates}?{query}"))!.Items;

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
