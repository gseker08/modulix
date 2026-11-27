using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Authorization;
using Modulix.Core.Contracts.Currency;
using Modulix.Core.Contracts.Records;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Records;
using Modulix.Shared.Settings;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Fatura kuru kuralı (ADR-0049 dilim 5d devamı): varsayılan kural fatura gününden önceki son yayımlanmış kuru verir (hafta sonu ve tatilde son iş günü),
/// firma "fatura günü" seçince fatura tarihinin kuru kullanılır; kur yoksa uydurulmaz; ayar firmaya özeldir ve yazmak ayar yönetimi izni ister.
/// </summary>
public class InvoiceRateRuleTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Key = "core.exchange-rates.invoice-rule";
    private static readonly DateOnly Friday = new(2026, 11, 20);
    private static readonly DateOnly Monday = new(2026, 11, 23);

    [Fact]
    public async Task Previous_day_rule_gives_the_last_published_rate_and_same_day_rule_gives_the_invoice_date_rate()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        await WriteRateAsync(admin, Friday, 30m);
        await WriteRateAsync(admin, Monday, 31m);

        // Varsayılan: Pazartesi faturası, önceki (Cuma) kuru alır; Pazar günü kur yoktur, en yeni önceki kur Cuma'nındır.
        var weekend = await ForInvoiceAsync(s.Tenant.Id, Monday);
        Assert.Equal((30m, Friday), (weekend!.Rate, weekend.RateDate));
        var tuesday = await ForInvoiceAsync(s.Tenant.Id, Monday.AddDays(1));
        Assert.Equal((31m, Monday), (tuesday!.Rate, tuesday.RateDate));

        await SaveRuleAsync(admin, "same-day");
        var sameDay = await ForInvoiceAsync(s.Tenant.Id, Monday);
        Assert.Equal((31m, Monday), (sameDay!.Rate, sameDay.RateDate));

        // Kur yoksa uydurulmaz: ilk kurdan önceki faturaya kur verilmez.
        Assert.Null(await ForInvoiceAsync(s.Tenant.Id, Friday.AddDays(-7)));
    }

    [Fact]
    public async Task The_rule_is_per_tenant_and_changing_it_needs_the_settings_permission()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        using var readerA = await LoginAsync(a.Reader);
        await WriteRateAsync(adminA, Friday, 30m);
        await WriteRateAsync(adminA, Monday, 31m);
        await WriteRateAsync(adminB, Friday, 30m);
        await WriteRateAsync(adminB, Monday, 31m);

        await SaveRuleAsync(adminA, "same-day");
        Assert.Equal(31m, (await ForInvoiceAsync(a.Tenant.Id, Monday))!.Rate);
        Assert.Equal(30m, (await ForInvoiceAsync(b.Tenant.Id, Monday))!.Rate);

        // Doğrulama: bilinmeyen değer reddedilir. Yetki: ayar izni olmayan değiştiremez.
        var current = (await adminA.GetFromJsonAsync<SettingsResponse>("/api/settings"))!.Settings.First(x => x.Key == Key);
        var invalid = await adminA.PutAsJsonAsync("/api/settings", new SaveSettingsRequest(new Dictionary<string, SettingWriteDto> { [Key] = new("yarin", current.Version) }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        var denied = await readerA.PutAsJsonAsync("/api/settings", new SaveSettingsRequest(new Dictionary<string, SettingWriteDto> { [Key] = new("previous-day", current.Version) }));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private async Task<ExchangeRateQuote?> ForInvoiceAsync(Guid tenantId, DateOnly invoiceDate)
    {
        using var scope = factory.OpenScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IExchangeRateReader>().FindForInvoiceAsync("USD", "TRY", invoiceDate, CancellationToken.None);
    }

    private static async Task SaveRuleAsync(HttpClient admin, string value)
    {
        var current = (await admin.GetFromJsonAsync<SettingsResponse>("/api/settings"))!.Settings.First(x => x.Key == Key);
        var response = await admin.PutAsJsonAsync("/api/settings", new SaveSettingsRequest(new Dictionary<string, SettingWriteDto> { [Key] = new(value, current.Version) }));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task WriteRateAsync(HttpClient admin, DateOnly day, decimal rate)
    {
        var response = await admin.PostAsJsonAsync("/api/records/core.exchange-rate", new RecordWriteRequest(new JsonObject
        {
            ["fromCurrency"] = "USD",
            ["toCurrency"] = "TRY",
            ["rate"] = rate,
            ["validOn"] = day.ToString("yyyy-MM-dd"),
            ["source"] = ExchangeRateSources.CentralBank,
        }));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<(Modulix.Core.Platform.Tenant Tenant, ModulixUser Admin, ModulixUser Reader)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        var admin = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, [], Scope.All, reader);
        return (tenant, admin, reader);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
