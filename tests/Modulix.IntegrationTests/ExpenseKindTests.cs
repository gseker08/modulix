using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Authorization;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Masraf ürün türü (ADR-0018, masraf kalemleri): katalogda "Expense" türü açılır ve süzülür, teklif satırına masraf türüyle gelir, masraf satırı
/// teklif geneli indirim almaz; tür tenant'a özeldir ve katalog yazma izni ister.
/// </summary>
public class ExpenseKindTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly string[] ReaderPermissions = [ProductsRead];

    [Fact]
    public async Task Expense_products_are_filterable_and_become_expense_lines_that_take_no_document_discount()
    {
        var s = await SetUpAsync();
        using var admin = await LoginAsync(s.Admin);
        var unit = await PostAsync<MeasureUnitDto>(admin, "/api/crm/units", new SaveMeasureUnitRequest("ADT", "Adet", "count"));
        var expense = await PostAsync<ProductDto>(admin, "/api/crm/products", new SaveProductRequest("MSR-ORDINO", "Ordino", unit.Id, "Expense", "Active"));
        await PostAsync<ProductDto>(admin, "/api/crm/products", new SaveProductRequest("URN-1", "Palet", unit.Id, "Product", "Active"));
        Assert.Equal("Expense", expense.Kind);

        var expenses = (await admin.GetFromJsonAsync<CrmPage<ProductDto>>("/api/crm/products?kind=Expense"))!.Items;
        Assert.Equal(["MSR-ORDINO"], expenses.Select(p => p.Code));

        var quote = await PostAsync<QuoteDto>(admin, "/api/crm/quotes", new SaveQuoteRequest("Masraflı teklif", s.CompanyId, ValidUntil: DateTimeOffset.UtcNow.AddDays(30)));
        await PostAsync<QuoteLineDto>(admin, "/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, "Hizmet bedeli", "Free", null, 1m, 100m));
        var line = await PostAsync<QuoteLineDto>(admin, "/api/crm/quote-lines", new SaveQuoteLineRequest(quote.Id, null, "Free", expense.Id, 1m, 100m));
        Assert.Equal("Expense", line.Kind);

        var current = (await admin.GetFromJsonAsync<QuoteDto>($"/api/crm/quotes/{quote.Id}"))!;
        Assert.Equal(200m, current.NetTotal);
        var discounted = await admin.PutAsJsonAsync($"/api/crm/quotes/{quote.Id}", new SaveQuoteRequest(current.Title, current.CompanyId, Version: current.Version,
            ValidUntil: current.ValidUntil, DocumentDiscountKind: "Percent", DocumentDiscountValue: 10m));
        Assert.True(discounted.StatusCode == HttpStatusCode.OK, await discounted.Content.ReadAsStringAsync());
        var after = (await discounted.Content.ReadFromJsonAsync<QuoteDto>())!;
        Assert.Equal(190m, after.NetTotal); // yalnızca hizmet bedeline %10: 90 + 100
        var lines = (await admin.GetFromJsonAsync<CrmPage<QuoteLineDto>>($"/api/crm/quote-lines?quoteId={quote.Id}"))!.Items;
        Assert.Equal(0m, lines.Single(l => l.Kind == "Expense").DocumentDiscountShare);
        Assert.Equal(10m, lines.Single(l => l.Kind == "Free").DocumentDiscountShare);
    }

    [Fact]
    public async Task Invalid_kind_is_rejected_and_the_expense_catalog_is_tenant_scoped_and_permissioned()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        using var adminA = await LoginAsync(a.Admin);
        using var adminB = await LoginAsync(b.Admin);
        using var readerA = await LoginAsync(a.Reader);
        var unit = await PostAsync<MeasureUnitDto>(adminA, "/api/crm/units", new SaveMeasureUnitRequest("ADT", "Adet", "count"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await adminA.PostAsJsonAsync("/api/crm/products", new SaveProductRequest("MSR-X", "Geçersiz", unit.Id, "Masraf", "Active"))).StatusCode);
        var expense = await PostAsync<ProductDto>(adminA, "/api/crm/products", new SaveProductRequest("MSR-DAMGA", "Damga vergisi", unit.Id, "Expense", "Active"));

        Assert.Empty((await adminB.GetFromJsonAsync<CrmPage<ProductDto>>("/api/crm/products?kind=Expense"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.GetAsync($"/api/crm/products/{expense.Id}")).StatusCode);

        // Yetki: yalnız katalog okuma izni olan masraf kalemi oluşturamaz, ama görür.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await readerA.PostAsJsonAsync("/api/crm/products", new SaveProductRequest("MSR-Y", "Yetkisiz", unit.Id, "Expense", "Active"))).StatusCode);
        Assert.Equal(["MSR-DAMGA"], (await readerA.GetFromJsonAsync<CrmPage<ProductDto>>("/api/crm/products?kind=Expense"))!.Items.Select(p => p.Code));
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<(ModulixUser Admin, ModulixUser Reader, Guid CompanyId)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        var reader = await factory.CreateUserAsync(tenant);
        using (var scope = factory.OpenScope(tenant.Id))
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleService>();
            var system = await roles.EnsureSystemRoleAsync(CancellationToken.None);
            Assert.True(await roles.SetMembershipRolesAsync(await factory.GetMembershipIdAsync(admin.Id, tenant.Id), [system.Id], CancellationToken.None));
        }

        await factory.CreateRoleAsync(tenant.Id, ReaderPermissions, Scope.All, reader);
        using var client = await LoginAsync(admin);
        var company = await PostAsync<CompanyDto>(client, "/api/crm/companies", new SaveCompanyRequest("Anka Ltd."));
        return (admin, reader, company.Id);
    }

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }
}
