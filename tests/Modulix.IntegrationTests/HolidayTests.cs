using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Calendar;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Records;
using static Modulix.Core.Modules.CoreModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0049 dilim 6a: firmanın resmî tatil listesi Core'da (<c>core.holiday</c>). Mutlu yol (ekleme, yıl süzgeci, düzeltme, silme, sözleşmeden okuma),
/// doğrulama (biçim, zorunlu alan, aynı tarih), tenant izolasyonu (başka tenant'ın günü görünmez ve değişmez; aynı tarih başka tenant'ta serbest) ve
/// yetki (izinsiz üye okur ama yazamaz; izinli rol yazar). İK'nın izin gün hesabı bu listeyi kullanır (<see cref="HrLeaveTests"/>).
/// </summary>
public class HolidayTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Holidays = "/api/records/" + CalendarEntities.Holiday;

    [Fact]
    public async Task Holidays_are_added_listed_by_year_corrected_removed_and_read_through_the_contract()
    {
        var s = await SetUpAsync();
        using var keeper = await LoginAsync(s.Keeper);

        var republic = await PostAsync(keeper, new JsonObject { ["date"] = "2027-10-29", ["name"] = "Cumhuriyet Bayramı" });
        var eve = await PostAsync(keeper, new JsonObject { ["date"] = "2027-10-28", ["name"] = " Arife ", ["halfDay"] = true });
        await PostAsync(keeper, new JsonObject { ["date"] = "2028-01-01", ["name"] = "Yılbaşı" });
        Assert.Equal(("Arife", true), (eve.Values["name"]!.GetValue<string>(), eve.Values["halfDay"]!.GetValue<bool>()));

        var year = await ListAsync(keeper, "year=2027");
        Assert.Equal(["2027-10-28", "2027-10-29"], year.Select(h => h.Values["date"]!.GetValue<string>()));
        Assert.Equal(["Yılbaşı"], (await ListAsync(keeper, "from=2027-12-01")).Select(h => h.Values["name"]!.GetValue<string>()));

        var renamed = await keeper.PutAsJsonAsync($"{Holidays}/{republic.Id}",
            new RecordWriteRequest(new JsonObject { ["name"] = "Cumhuriyet Bayramı (29 Ekim)" }, republic.Version));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await keeper.DeleteAsync($"{Holidays}/{eve.Id}?version={eve.Version}")).StatusCode);

        using var scope = factory.OpenScope(s.TenantId);
        var days = await scope.ServiceProvider.GetRequiredService<IHolidayCalendar>()
            .GetAsync(new DateOnly(2027, 10, 1), new DateOnly(2027, 12, 31), CancellationToken.None);
        var day = Assert.Single(days).Value;
        Assert.Equal((new DateOnly(2027, 10, 29), "Cumhuriyet Bayramı (29 Ekim)", false), (day.Date, day.Name, day.HalfDay));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IHolidayCalendar>()
            .GetAsync(new DateOnly(2027, 12, 31), new DateOnly(2027, 1, 1), CancellationToken.None));
    }

    [Fact]
    public async Task Malformed_incomplete_and_duplicate_days_are_rejected()
    {
        var s = await SetUpAsync();
        using var keeper = await LoginAsync(s.Keeper);
        await PostAsync(keeper, new JsonObject { ["date"] = "2027-05-19", ["name"] = "Gençlik ve Spor Bayramı" });

        var malformed = await ProblemAsync(keeper, new JsonObject { ["date"] = "19.05.2027", ["name"] = "X", ["color"] = "red" });
        Assert.True(malformed.Errors.ContainsKey("date"));
        Assert.True(malformed.Errors.ContainsKey("color"));
        var missing = await ProblemAsync(keeper, new JsonObject { ["date"] = "2027-05-20" });
        Assert.True(missing.Errors.ContainsKey("name"));
        var duplicate = await ProblemAsync(keeper, new JsonObject { ["date"] = "2027-05-19", ["name"] = "Tekrar" });
        Assert.Equal(["Bu tarih tatil listesinde zaten var."], duplicate.Errors["date"]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await keeper.GetAsync($"{Holidays}?year=abc")).StatusCode);
    }

    [Fact]
    public async Task Another_tenants_days_are_invisible_and_untouchable_and_the_same_date_is_free_there()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var keeperA = await LoginAsync(a.Keeper);
        using var keeperB = await LoginAsync(b.Keeper);
        var dayA = await PostAsync(keeperA, new JsonObject { ["date"] = "2027-04-23", ["name"] = "Ulusal Egemenlik ve Çocuk Bayramı" });

        Assert.DoesNotContain(await ListAsync(keeperB, null), h => h.Id == dayA.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await keeperB.GetAsync($"{Holidays}/{dayA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await keeperB.PutAsJsonAsync($"{Holidays}/{dayA.Id}",
            new RecordWriteRequest(new JsonObject { ["name"] = "Değişti" }, dayA.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await keeperB.DeleteAsync($"{Holidays}/{dayA.Id}?version={dayA.Version}")).StatusCode);
        await PostAsync(keeperB, new JsonObject { ["date"] = "2027-04-23", ["name"] = "Ulusal Egemenlik ve Çocuk Bayramı" });

        using var scope = factory.OpenScope(b.TenantId);
        var days = await scope.ServiceProvider.GetRequiredService<IHolidayCalendar>()
            .GetAsync(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), CancellationToken.None);
        Assert.Single(days);
    }

    [Fact]
    public async Task Members_read_the_list_but_only_the_holiday_permission_writes_it()
    {
        var s = await SetUpAsync();
        using var keeper = await LoginAsync(s.Keeper);
        using var member = await LoginAsync(s.Member);
        var day = await PostAsync(keeper, new JsonObject { ["date"] = "2027-08-30", ["name"] = "Zafer Bayramı" });

        Assert.Contains(await ListAsync(member, null), h => h.Id == day.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(Holidays,
            new RecordWriteRequest(new JsonObject { ["date"] = "2027-08-31", ["name"] = "Benim tatilim" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync($"{Holidays}/{day.Id}",
            new RecordWriteRequest(new JsonObject { ["name"] = "Değişti" }, day.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync($"{Holidays}/{day.Id}?version={day.Version}")).StatusCode);
    }

    // --- yardımcılar ---

    private sealed record Setup(Guid TenantId, ModulixUser Keeper, ModulixUser Member);

    /// <summary>Tatil listesini yöneten rol ("keeper") ve rolü olmayan üye.</summary>
    private async Task<Setup> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        var keeper = await factory.CreateUserAsync(tenant);
        var member = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ManageHolidays], Scope.All, keeper);
        return new Setup(tenant.Id, keeper, member);
    }

    private static async Task<RecordResponse> PostAsync(HttpClient client, JsonObject values)
    {
        var response = await client.PostAsJsonAsync(Holidays, new RecordWriteRequest(values));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RecordResponse>())!;
    }

    private static async Task<ValidationProblemDetails> ProblemAsync(HttpClient client, JsonObject values)
    {
        var response = await client.PostAsJsonAsync(Holidays, new RecordWriteRequest(values));
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
    }

    private static async Task<IReadOnlyList<RecordResponse>> ListAsync(HttpClient client, string? query) =>
        (await client.GetFromJsonAsync<RecordPageResponse>(query is null ? Holidays : $"{Holidays}?{query}"))!.Items;

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
