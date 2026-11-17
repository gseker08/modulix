using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Modulix.Core.Contracts.SharedRecords;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Records;
using static Modulix.Modules.Crm.CrmModule.Permissions;
using Scope = Modulix.Core.Contracts.Authorization.RecordScope;

namespace Modulix.IntegrationTests;

/// <summary>
/// Kişinin ve firmanın yazışma dili (ADR-0034 §6): CRM kişisi ve firması dili ortak kayda (Person, Party) yazar, boş değer
/// temizler (tenant varsayılanı), desteklenmeyen kod reddedilir; başka tenant'ın kaydının dili okunamaz ve değiştirilemez;
/// yazma izni olmayan kullanıcı dili değiştiremez.
/// </summary>
public class CorrespondenceLanguageTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private const string Companies = "/api/crm/companies";
    private const string Contacts = "/api/crm/contacts";

    [Fact]
    public async Task Company_and_contact_languages_are_stored_on_the_shared_records_and_can_be_cleared()
    {
        var (_, admin) = await SetUpAsync();
        using var client = await LoginAsync(admin);

        var company = await PostAsync<CompanyDto>(client, Companies, new SaveCompanyRequest("Baku Trade LLC", PreferredLanguage: "az"));
        var contact = await PostAsync<ContactDto>(client, Contacts,
            new SaveContactRequest("John", "Smith", CompanyId: company.Id, PreferredLanguage: "en"));
        Assert.Equal("az", company.PreferredLanguage);
        Assert.Equal("en", contact.PreferredLanguage);

        var party = await client.GetFromJsonAsync<RecordResponse>($"/api/records/{SharedEntities.Party}/{company.PartyId}");
        var person = await client.GetFromJsonAsync<RecordResponse>($"/api/records/{SharedEntities.Person}/{contact.PersonId}");
        Assert.Equal("az", party!.Values["preferredLanguage"]!.GetValue<string>());
        Assert.Equal("en", person!.Values["preferredLanguage"]!.GetValue<string>());

        // Boş dil temizler: kişi firmanın diline, firma tenant varsayılanına düşer.
        var cleared = await client.PutAsJsonAsync($"{Contacts}/{contact.Id}",
            new SaveContactRequest("John", "Smith", CompanyId: company.Id, Version: contact.Version));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<ContactDto>())!.PreferredLanguage);

        // Ortak kayıt üzerinden yazılan dil CRM firmasında görünür (büyük harf normalleşir).
        var changed = await client.PutAsJsonAsync($"/api/records/{SharedEntities.Party}/{company.PartyId}",
            new RecordWriteRequest(new JsonObject { ["preferredLanguage"] = "EN" }, party.Version));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("en", (await client.GetFromJsonAsync<CompanyDto>($"{Companies}/{company.Id}"))!.PreferredLanguage);
    }

    [Fact]
    public async Task An_unsupported_language_code_is_rejected()
    {
        var (_, admin) = await SetUpAsync();
        using var client = await LoginAsync(admin);

        var company = await client.PostAsJsonAsync(Companies, new SaveCompanyRequest("Berlin GmbH", PreferredLanguage: "de"));
        Assert.Contains("preferredLanguage", (await ValidationProblemAsync(company)).Errors.Keys);

        var contact = await client.PostAsJsonAsync(Contacts, new SaveContactRequest("Hans", "Muller", PreferredLanguage: "tr-TR"));
        Assert.Contains("preferredLanguage", (await ValidationProblemAsync(contact)).Errors.Keys);

        var person = await client.PostAsJsonAsync($"/api/records/{SharedEntities.Person}",
            new RecordWriteRequest(new JsonObject { ["firstName"] = "Eva", ["lastName"] = "Klein", ["preferredLanguage"] = "deutsch" }));
        Assert.Contains("preferredLanguage", (await ValidationProblemAsync(person)).Errors.Keys);
    }

    [Fact]
    public async Task Another_tenants_company_language_can_neither_be_read_nor_changed()
    {
        var (_, adminA) = await SetUpAsync();
        var (_, adminB) = await SetUpAsync();
        using var clientA = await LoginAsync(adminA);
        using var clientB = await LoginAsync(adminB);
        var company = await PostAsync<CompanyDto>(clientA, Companies, new SaveCompanyRequest("Yalniz A Firmasi", PreferredLanguage: "en"));

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"{Companies}/{company.Id}")).StatusCode);
        var write = await clientB.PutAsJsonAsync($"{Companies}/{company.Id}",
            new SaveCompanyRequest("Yalniz A Firmasi", Version: company.Version, PreferredLanguage: "az"));
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/records/{SharedEntities.Party}/{company.PartyId}")).StatusCode);

        Assert.Equal("en", (await clientA.GetFromJsonAsync<CompanyDto>($"{Companies}/{company.Id}"))!.PreferredLanguage);
    }

    [Fact]
    public async Task A_user_without_write_permission_cannot_change_the_language()
    {
        var (tenant, admin) = await SetUpAsync();
        var reader = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ContactsRead], Scope.All, reader);
        using var adminClient = await LoginAsync(admin);
        var contact = await PostAsync<ContactDto>(adminClient, Contacts, new SaveContactRequest("Okan", "Okur", PreferredLanguage: "tr"));
        using var client = await LoginAsync(reader);

        var write = await client.PutAsJsonAsync($"{Contacts}/{contact.Id}",
            new SaveContactRequest("Okan", "Okur", Version: contact.Version, PreferredLanguage: "en"));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal("tr", (await client.GetFromJsonAsync<ContactDto>($"{Contacts}/{contact.Id}"))!.PreferredLanguage);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<ValidationProblemDetails> ValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
    }

    private async Task<(Core.Platform.Tenant Tenant, ModulixUser Admin)> SetUpAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
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
}
