using System.Net;
using System.Net.Http.Json;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Orders;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Müşteri zaman çizelgesinin sipariş kaynağı (ADR-0053, dilim 2): müşterinin siparişleri CRM kayıtlarıyla aynı akışta görünür. Kaynak Sipariş
/// modülündedir; satırlar siparişin kendi izni, veri kapsamı ve lisansıyla süzülür.
/// </summary>
public class CustomerTimelineOrderTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string OrdersSource = "orders.orders";

    [Fact]
    public async Task Timeline_includes_the_customers_orders_next_to_crm_records()
    {
        var (_, admin) = await TenantWithAdminAsync(orders: true);
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Sipariş A.Ş.");
        var order = await CreateOrderAsync(client, company.Id);

        // Başka müşterinin siparişi çizelgeye girmez.
        var other = await CreateCompanyAsync(client, "Başka Ltd.");
        await CreateOrderAsync(client, other.Id);

        var timeline = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.Contains(OrdersSource, timeline.Sources);

        // Sipariş tekliften sonra oluştu: en üstteki satırdır.
        var row = timeline.Items[0];
        Assert.Equal(OrdersAccess.OrderEntityKey, row.EntityKey);
        Assert.Equal(order.Id, row.RecordId);
        Assert.Equal(order.Title, row.Title);
        Assert.Equal(order.Number, row.Reference);
        Assert.Equal(order.Status, row.Status);
        Assert.Equal(admin.Id, row.OwnerId);
        Assert.Single(timeline.Items, i => i.EntityKey == OrdersAccess.OrderEntityKey);
        Assert.Contains(timeline.Items, i => i.EntityKey == CrmEntityKeys.Quote);

        // İmleç sipariş satırını da kapsar: tek satırlık sayfalar bütün çizelgeyi sırayla verir.
        var first = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=1"))!;
        Assert.Equal(order.Id, Assert.Single(first.Items).RecordId);
        var before = Uri.EscapeDataString(first.NextBefore!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var second = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=1&before={before}"))!;
        Assert.Equal(timeline.Items[1].RecordId, Assert.Single(second.Items).RecordId);
    }

    [Fact]
    public async Task Timeline_has_no_order_source_when_the_orders_module_is_off()
    {
        var (_, admin) = await TenantWithAdminAsync(orders: false);
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Modülsüz A.Ş.");

        var timeline = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.DoesNotContain(OrdersSource, timeline.Sources);
        Assert.Empty(timeline.Items);
    }

    [Fact]
    public async Task Timeline_never_shows_another_tenants_orders()
    {
        var (_, adminA) = await TenantWithAdminAsync(orders: true);
        var (_, adminB) = await TenantWithAdminAsync(orders: true);
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var companyA = await CreateCompanyAsync(clientA, "A Tenant A.Ş.");
        await CreateOrderAsync(clientA, companyA.Id);
        var companyB = await CreateCompanyAsync(clientB, "B Tenant A.Ş.");

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/crm/companies/{companyA.Id}/timeline")).StatusCode);
        var own = (await clientB.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{companyB.Id}/timeline"))!;
        Assert.Contains(OrdersSource, own.Sources);
        Assert.Empty(own.Items);
    }

    [Fact]
    public async Task Timeline_order_rows_follow_the_order_permission_and_data_scope()
    {
        var (tenant, admin) = await TenantWithAdminAsync(orders: true);
        using var adminClient = await LoginAsync(admin);
        var company = await CreateCompanyAsync(adminClient, "Yetki A.Ş.");
        await CreateOrderAsync(adminClient, company.Id);

        // Sipariş okuma izni yok: kaynak da satır da yok; teklif satırı yine görünür.
        var crmOnly = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, QuotesRead], Scope.All, crmOnly);
        using var crmOnlyClient = await LoginAsync(crmOnly);
        var withoutOrders = (await crmOnlyClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.DoesNotContain(OrdersSource, withoutOrders.Sources);
        Assert.DoesNotContain(withoutOrders.Items, i => i.EntityKey == OrdersAccess.OrderEntityKey);
        Assert.Contains(withoutOrders.Items, i => i.EntityKey == CrmEntityKeys.Quote);

        // İzin var, kapsam "kendi": kaynak okunur ama başkasının siparişi gelmez.
        var ownOnly = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, CompaniesWrite, OrdersAccess.OrdersRead], Scope.Own, ownOnly);
        using var ownOnlyClient = await LoginAsync(ownOnly);
        var ownCompany = await CreateCompanyAsync(ownOnlyClient, "Temsilcinin firması");
        await CreateOrderAsync(adminClient, ownCompany.Id);
        var scoped = (await ownOnlyClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{ownCompany.Id}/timeline"))!;
        Assert.Contains(OrdersSource, scoped.Sources);
        Assert.Empty(scoped.Items);

        // Kapsam "tümü": sipariş satırı gelir.
        var reader = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, OrdersAccess.OrdersRead], Scope.All, reader);
        using var readerClient = await LoginAsync(reader);
        var visible = (await readerClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.Equal([OrdersAccess.OrderEntityKey], visible.Items.Select(i => i.EntityKey));
    }

    private async Task<(Core.Platform.Tenant Tenant, ModulixUser Admin)> TenantWithAdminAsync(bool orders)
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        if (orders)
        {
            await factory.SetModuleAsync(tenant.Id, "orders", enabled: true);
        }

        var admin = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, admin);
        return (tenant, admin);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<CompanyDto> CreateCompanyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/crm/companies", new SaveCompanyRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
    }

    /// <summary>Sipariş yalnızca kabul edilmiş tekliften oluşur (ADR-0023): teklif açılır, sunulur, kabul kaydedilir, sipariş üretilir.</summary>
    private static async Task<OrderDto> CreateOrderAsync(HttpClient client, Guid companyId)
    {
        var create = await client.PostAsJsonAsync("/api/crm/quotes", new SaveQuoteRequest("Cihaz teklifi", companyId, ValidUntil: DateTimeOffset.UtcNow.AddDays(30)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var quote = (await create.Content.ReadFromJsonAsync<QuoteDto>())!;
        var line = await client.PostAsJsonAsync("/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, "Cihaz", "Free", null, 2m, 1000m, null, "None", 0m, 20m, null, "Standard"));
        Assert.True(line.StatusCode == HttpStatusCode.Created, await line.Content.ReadAsStringAsync());
        foreach (var action in new[] { "present", "record-accepted" })
        {
            var current = (await client.GetFromJsonAsync<QuoteDto>($"/api/crm/quotes/{quote.Id}"))!;
            var moved = await client.PostAsJsonAsync($"/api/crm/quotes/{quote.Id}/{action}", new QuoteTransitionRequest(current.Version));
            Assert.True(moved.StatusCode == HttpStatusCode.OK, $"{action}: {await moved.Content.ReadAsStringAsync()}");
        }

        var response = await client.PostAsJsonAsync($"/api/orders/from-quote/{quote.Id}", new CreateOrderFromQuoteRequest());
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
    }
}
