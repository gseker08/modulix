using System.Net;
using System.Net.Http.Json;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Contracts;
using Modulix.Shared.Crm;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Müşteri zaman çizelgesinin sözleşme kaynağı (ADR-0053, dilim 2): müşterinin sözleşmeleri CRM kayıtlarıyla aynı akışta görünür. Kaynak
/// Sözleşme modülündedir; satırlar sözleşmenin kendi izni, veri kapsamı ve lisansıyla süzülür.
/// </summary>
public class CustomerTimelineContractTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string ContractsSource = "contracts.contracts";

    [Fact]
    public async Task Timeline_includes_the_customers_contracts_next_to_crm_records()
    {
        var (_, admin) = await TenantWithAdminAsync(contracts: true);
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Sözleşme A.Ş.");
        var opportunity = await client.PostAsJsonAsync("/api/crm/opportunities",
            new SaveOpportunityRequest("Bakım fırsatı", company.Id, Stage: "Lead", Amount: 100m, Currency: "TRY"));
        Assert.Equal(HttpStatusCode.Created, opportunity.StatusCode);
        var contract = await CreateContractAsync(client, company.PartyId!.Value, "Yıllık bakım sözleşmesi");

        // Başka müşterinin sözleşmesi çizelgeye girmez.
        var other = await CreateCompanyAsync(client, "Başka Ltd.");
        await CreateContractAsync(client, other.PartyId!.Value, "İlgisiz sözleşme");

        var timeline = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.Contains(ContractsSource, timeline.Sources);
        Assert.Equal([ContractsAccess.ContractEntityKey, CrmEntityKeys.Opportunity], timeline.Items.Select(i => i.EntityKey));

        // Sözleşme fırsattan sonra oluştu: en üstteki satırdır.
        var row = timeline.Items[0];
        Assert.Equal(contract.Id, row.RecordId);
        Assert.Equal("Yıllık bakım sözleşmesi", row.Title);
        Assert.Equal(contract.Number, row.Reference);
        Assert.Equal(contract.EffectiveStatus, row.Status);
        Assert.Equal(admin.Id, row.OwnerId);

        // İmleç sözleşme satırını da kapsar.
        var first = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=1"))!;
        Assert.Equal(contract.Id, Assert.Single(first.Items).RecordId);
        var before = Uri.EscapeDataString(first.NextBefore!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var second = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline?take=1&before={before}"))!;
        Assert.Equal(CrmEntityKeys.Opportunity, Assert.Single(second.Items).EntityKey);
        Assert.Null(second.NextBefore);
    }

    [Fact]
    public async Task Timeline_has_no_contract_source_when_the_contracts_module_is_off()
    {
        var (_, admin) = await TenantWithAdminAsync(contracts: false);
        using var client = await LoginAsync(admin);
        var company = await CreateCompanyAsync(client, "Modülsüz A.Ş.");

        var timeline = (await client.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.DoesNotContain(ContractsSource, timeline.Sources);
        Assert.Empty(timeline.Items);
    }

    [Fact]
    public async Task Timeline_never_shows_another_tenants_contracts()
    {
        var (_, adminA) = await TenantWithAdminAsync(contracts: true);
        var (_, adminB) = await TenantWithAdminAsync(contracts: true);
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var companyA = await CreateCompanyAsync(clientA, "A Tenant A.Ş.");
        await CreateContractAsync(clientA, companyA.PartyId!.Value, "A'nın sözleşmesi");
        var companyB = await CreateCompanyAsync(clientB, "B Tenant A.Ş.");

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/crm/companies/{companyA.Id}/timeline")).StatusCode);
        var own = (await clientB.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{companyB.Id}/timeline"))!;
        Assert.Contains(ContractsSource, own.Sources);
        Assert.Empty(own.Items);
    }

    [Fact]
    public async Task Timeline_contract_rows_follow_the_contract_permission_and_data_scope()
    {
        var (tenant, admin) = await TenantWithAdminAsync(contracts: true);
        using var adminClient = await LoginAsync(admin);
        var company = await CreateCompanyAsync(adminClient, "Yetki A.Ş.");
        await CreateContractAsync(adminClient, company.PartyId!.Value, "Yöneticinin sözleşmesi");

        // Sözleşme okuma izni yok: kaynak da satır da yok.
        var crmOnly = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead], Scope.All, crmOnly);
        using var crmOnlyClient = await LoginAsync(crmOnly);
        var withoutContracts = (await crmOnlyClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.DoesNotContain(ContractsSource, withoutContracts.Sources);
        Assert.Empty(withoutContracts.Items);

        // İzin var, kapsam "kendi": kaynak okunur ama başkasının sözleşmesi gelmez.
        var ownOnly = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, CompaniesWrite, ContractsAccess.ContractsRead], Scope.Own, ownOnly);
        using var ownOnlyClient = await LoginAsync(ownOnly);
        var ownCompany = await CreateCompanyAsync(ownOnlyClient, "Temsilcinin firması");
        await CreateContractAsync(adminClient, ownCompany.PartyId!.Value, "Yöneticinin açtığı sözleşme");
        var scoped = (await ownOnlyClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{ownCompany.Id}/timeline"))!;
        Assert.Contains(ContractsSource, scoped.Sources);
        Assert.Empty(scoped.Items);

        // Kapsam "tümü": sözleşme satırı gelir.
        var reader = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [CompaniesRead, ContractsAccess.ContractsRead], Scope.All, reader);
        using var readerClient = await LoginAsync(reader);
        var visible = (await readerClient.GetFromJsonAsync<CustomerTimelineDto>($"/api/crm/companies/{company.Id}/timeline"))!;
        Assert.Equal([ContractsAccess.ContractEntityKey], visible.Items.Select(i => i.EntityKey));
    }

    private async Task<(Core.Platform.Tenant Tenant, ModulixUser Admin)> TenantWithAdminAsync(bool contracts)
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        if (contracts)
        {
            await factory.SetModuleAsync(tenant.Id, "contracts", enabled: true);
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

    private static async Task<ContractDto> CreateContractAsync(HttpClient client, Guid partyId, string title)
    {
        var response = await client.PostAsJsonAsync("/api/contracts/contracts", new SaveContractRequest(title, partyId));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ContractDto>())!;
    }
}
