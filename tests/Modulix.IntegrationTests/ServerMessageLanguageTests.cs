using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Modulix.Core.Platform;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Account;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Modulix.Shared.Settings;
using static Modulix.Core.Modules.CoreModule.Permissions;
using static Modulix.Modules.Crm.CrmModule.Permissions;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0034 §6, dilim 18a: API hata iletileri isteğin dilinde döner. Dil oturumdan çözülür (kullanıcı tercihi → firma varsayılanı →
/// Türkçe); <c>Accept-Language</c> dikkate alınmaz. Hata kodu, alan adları ve yanıtın biçimi dile göre değişmez.
/// </summary>
public class ServerMessageLanguageTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private static readonly string[] SalesAccess = [CompaniesRead, CompaniesWrite];

    [Fact]
    public async Task Validation_error_comes_in_the_users_language_with_the_same_code_and_fields()
    {
        var tenant = await CrmTenantAsync();
        var english = await factory.CreateUserAsync(tenant);
        var turkish = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, SalesAccess, english, turkish);
        using var englishClient = await LoginAsync(english, "en");
        using var turkishClient = await LoginAsync(turkish);

        var inEnglish = await InvalidCompanyAsync(englishClient);
        var inTurkish = await InvalidCompanyAsync(turkishClient);

        Assert.Equal(("ValidationFailed", "Validation failed.", "Unknown field."), Summary(inEnglish));
        Assert.Equal(("ValidationFailed", "Doğrulama hatası.", "Bilinmeyen alan."), Summary(inTurkish));
    }

    [Fact]
    public async Task Tenant_default_applies_without_a_user_preference_and_does_not_leak_to_another_tenant()
    {
        var tenant = await CrmTenantAsync();
        var other = await CrmTenantAsync();
        var manager = await factory.CreateUserAsync(tenant);
        var member = await factory.CreateUserAsync(tenant);
        var english = await factory.CreateUserAsync(tenant);
        var outsider = await factory.CreateUserAsync(other);
        await factory.CreateRoleAsync(tenant.Id, [ManageSettings], manager);
        await factory.CreateRoleAsync(tenant.Id, SalesAccess, member, english);
        await factory.CreateRoleAsync(other.Id, SalesAccess, outsider);
        using var managerClient = await LoginAsync(manager);
        var saved = await managerClient.PutAsJsonAsync("/api/settings", new SaveSettingsRequest(
            new Dictionary<string, SettingWriteDto> { [SettingKeys.Language] = new("az", 0) }));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var memberClient = await LoginAsync(member);
        using var englishClient = await LoginAsync(english, "en");
        using var outsiderClient = await LoginAsync(outsider);

        Assert.Equal(("ValidationFailed", "Yoxlama xətası.", "Naməlum sahə."), Summary(await InvalidCompanyAsync(memberClient)));
        Assert.Equal(("ValidationFailed", "Validation failed.", "Unknown field."), Summary(await InvalidCompanyAsync(englishClient)));
        Assert.Equal(("ValidationFailed", "Doğrulama hatası.", "Bilinmeyen alan."), Summary(await InvalidCompanyAsync(outsiderClient)));
    }

    [Fact]
    public async Task Permission_error_is_translated_and_the_status_stays_forbidden()
    {
        var tenant = await CrmTenantAsync();
        var english = await factory.CreateUserAsync(tenant);
        var turkish = await factory.CreateUserAsync(tenant);
        using var englishClient = await LoginAsync(english, "en");
        using var turkishClient = await LoginAsync(turkish);

        var inEnglish = await englishClient.GetAsync("/api/crm/companies");
        var inTurkish = await turkishClient.GetAsync("/api/crm/companies");

        Assert.Equal(HttpStatusCode.Forbidden, inEnglish.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, inTurkish.StatusCode);
        var englishBody = await ProblemAsync(inEnglish);
        var turkishBody = await ProblemAsync(inTurkish);
        Assert.Equal("Not authorized.", englishBody["title"]!.GetValue<string>());
        Assert.Equal($"This action requires the '{CompaniesRead}' permission.", englishBody["detail"]!.GetValue<string>());
        Assert.Equal("Yetkiniz yok.", turkishBody["title"]!.GetValue<string>());
        Assert.Equal($"Bu işlem için '{CompaniesRead}' izni gerekir.", turkishBody["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task Accept_language_header_does_not_choose_the_language()
    {
        var tenant = await CrmTenantAsync();
        var turkish = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, SalesAccess, turkish);
        using var client = await LoginAsync(turkish);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        Assert.Equal(("ValidationFailed", "Doğrulama hatası.", "Bilinmeyen alan."), Summary(await InvalidCompanyAsync(client)));
    }

    /// <summary>Dilim 18d-1: CRM'in kendi alan iletisi (vergi numarası uzunluğu) okurun dilinde; kod ve alan adı aynı.</summary>
    [Fact]
    public async Task Crm_field_error_comes_in_the_users_language()
    {
        var tenant = await CrmTenantAsync();
        var english = await factory.CreateUserAsync(tenant);
        var turkish = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, SalesAccess, english, turkish);
        using var englishClient = await LoginAsync(english, "en");
        using var turkishClient = await LoginAsync(turkish);

        var inEnglish = await ShortTaxNumberAsync(englishClient);
        var inTurkish = await ShortTaxNumberAsync(turkishClient);

        Assert.Equal("ValidationFailed", inEnglish["code"]!.GetValue<string>());
        Assert.Equal("ValidationFailed", inTurkish["code"]!.GetValue<string>());
        Assert.Equal("The tax number must be 10 digits.", inEnglish["errors"]!["taxNumber"]![0]!.GetValue<string>());
        Assert.Equal("Vergi numarası 10 haneli olmalıdır.", inTurkish["errors"]!["taxNumber"]![0]!.GetValue<string>());
    }

    /// <summary>Dilim 18d-1: müşteri adayı iş akışının iletisi hem başlıkta hem alan hatasında okurun dilinde.</summary>
    [Fact]
    public async Task Crm_lead_workflow_error_comes_in_the_users_language()
    {
        var tenant = await CrmTenantAsync();
        var english = await factory.CreateUserAsync(tenant);
        var turkish = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [LeadsRead, LeadsDisqualify], english, turkish);
        using var englishClient = await LoginAsync(english, "en");
        using var turkishClient = await LoginAsync(turkish);

        var inEnglish = await DisqualifyWithoutReasonAsync(englishClient);
        var inTurkish = await DisqualifyWithoutReasonAsync(turkishClient);

        Assert.Equal(("ValidationFailed", "A disqualification reason is required."),
            (inEnglish["code"]!.GetValue<string>(), inEnglish["errors"]!["disqualificationReason"]![0]!.GetValue<string>()));
        Assert.Equal("A disqualification reason is required.", inEnglish["title"]!.GetValue<string>());
        Assert.Equal(("ValidationFailed", "Eleme nedeni zorunludur."),
            (inTurkish["code"]!.GetValue<string>(), inTurkish["errors"]!["disqualificationReason"]![0]!.GetValue<string>()));
        Assert.Equal("Eleme nedeni zorunludur.", inTurkish["title"]!.GetValue<string>());
    }

    /// <summary>Dilim 18d-1b: aktivite toplu işleminin problem yanıtı ve kayıt başına başarısızlık nedeni okurun dilinde.</summary>
    [Fact]
    public async Task Crm_bulk_activity_messages_come_in_the_users_language()
    {
        var tenant = await CrmTenantAsync();
        var english = await factory.CreateUserAsync(tenant);
        var turkish = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [ActivitiesRead], english, turkish);
        using var englishClient = await LoginAsync(english, "en");
        using var turkishClient = await LoginAsync(turkish);
        BulkActivityItem[] items = [new(Guid.NewGuid(), 1)];

        var unknownInEnglish = await englishClient.PostAsJsonAsync("/api/crm/activities/bulk", new BulkActivityRequest("yok", items));
        var unknownInTurkish = await turkishClient.PostAsJsonAsync("/api/crm/activities/bulk", new BulkActivityRequest("yok", items));
        Assert.Equal(HttpStatusCode.BadRequest, unknownInEnglish.StatusCode);
        var englishProblem = await ProblemAsync(unknownInEnglish);
        var turkishProblem = await ProblemAsync(unknownInTurkish);
        Assert.Equal(("Invalid request.", "Unknown bulk action."),
            (englishProblem["title"]!.GetValue<string>(), englishProblem["detail"]!.GetValue<string>()));
        Assert.Equal(("Geçersiz istek.", "Bilinmeyen toplu işlem."),
            (turkishProblem["title"]!.GetValue<string>(), turkishProblem["detail"]!.GetValue<string>()));

        var ownerlessInEnglish = await englishClient.PostAsJsonAsync("/api/crm/activities/bulk", new BulkActivityRequest("reassign", items));
        var ownerlessInTurkish = await turkishClient.PostAsJsonAsync("/api/crm/activities/bulk", new BulkActivityRequest("reassign", items));
        Assert.Equal("A new owner is required.", (await ownerlessInEnglish.Content.ReadFromJsonAsync<BulkActivityResultDto>())!.Failures.Single().Message);
        Assert.Equal("Yeni sorumlu zorunludur.", (await ownerlessInTurkish.Content.ReadFromJsonAsync<BulkActivityResultDto>())!.Failures.Single().Message);
    }

    /// <summary>Dilim 18d-1c: fiyatlandırma kaydının alan hatası (aynı para birimi çifti) okurun dilinde; kod ve alan adı aynı.</summary>
    [Fact]
    public async Task Crm_pricing_error_comes_in_the_users_language()
    {
        var tenant = await CrmTenantAsync();
        var english = await factory.CreateUserAsync(tenant);
        var turkish = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [PricesRead, PricesWrite], english, turkish);
        using var englishClient = await LoginAsync(english, "en");
        using var turkishClient = await LoginAsync(turkish);

        var inEnglish = await SameCurrencyRateAsync(englishClient);
        var inTurkish = await SameCurrencyRateAsync(turkishClient);

        Assert.Equal(("ValidationFailed", "The source and target currencies cannot be the same."),
            (inEnglish["code"]!.GetValue<string>(), inEnglish["errors"]!["toCurrency"]![0]!.GetValue<string>()));
        Assert.Equal(("ValidationFailed", "Kaynak ve hedef para birimi aynı olamaz."),
            (inTurkish["code"]!.GetValue<string>(), inTurkish["errors"]!["toCurrency"]![0]!.GetValue<string>()));
    }

    /// <summary>Dilim 18d-2: Sözleşmeler modülünün alan hatası (koşulsuz onay kuralı) okurun dilinde; kod ve alan adı aynı.</summary>
    [Fact]
    public async Task Contracts_field_error_comes_in_the_users_language()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "contracts", enabled: true);
        var english = await factory.CreateUserAsync(tenant);
        var turkish = await factory.CreateUserAsync(tenant);
        await factory.CreateRoleAsync(tenant.Id, [Modulix.Modules.Contracts.ContractsModule.Permissions.ApprovalsManage], english, turkish);
        using var englishClient = await LoginAsync(english, "en");
        using var turkishClient = await LoginAsync(turkish);

        var inEnglish = await UnconditionalRuleAsync(englishClient);
        var inTurkish = await UnconditionalRuleAsync(turkishClient);

        Assert.Equal(("ValidationFailed", "Enter at least one condition: amount, discount, term, non-standard clause, or risk level."),
            (inEnglish["code"]!.GetValue<string>(), inEnglish["errors"]!["minGrossTotal"]![0]!.GetValue<string>()));
        Assert.Equal(("ValidationFailed", "En az bir koşul girin: tutar, indirim, süre, standart dışı madde ya da risk düzeyi."),
            (inTurkish["code"]!.GetValue<string>(), inTurkish["errors"]!["minGrossTotal"]![0]!.GetValue<string>()));
    }

    private static async Task<JsonObject> UnconditionalRuleAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/contracts/approval-rules",
            new Modulix.Shared.Contracts.SaveContractApprovalRuleRequest("Dil kuralı"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        return await ProblemAsync(response);
    }

    private static async Task<JsonObject> SameCurrencyRateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/crm/exchange-rates", new SaveExchangeRateRequest("TRY", "TRY", 1m, new DateOnly(2026, 5, 30)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        return await ProblemAsync(response);
    }

    private static async Task<JsonObject> ShortTaxNumberAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/crm/companies", new SaveCompanyRequest("Dil Vergi A.Ş.", TaxNumber: "123"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        return await ProblemAsync(response);
    }

    private static async Task<JsonObject> DisqualifyWithoutReasonAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync($"/api/crm/leads/{Guid.NewGuid()}/disqualify", new DisqualifyLeadRequest(1, " "));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        return await ProblemAsync(response);
    }

    private async Task<Tenant> CrmTenantAsync()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        return tenant;
    }

    // Bilinmeyen özel alan: ortak boru hattının (çevrilen) alan hatası.
    private static async Task<JsonObject> InvalidCompanyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/crm/companies",
            new SaveCompanyRequest("Dil A.Ş.", Custom: new JsonObject { ["yok_boyle"] = 1 }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        return await ProblemAsync(response);
    }

    private static (string Code, string Title, string Field) Summary(JsonObject problem) => (
        problem["code"]!.GetValue<string>(),
        problem["title"]!.GetValue<string>(),
        problem["errors"]!["custom.yok_boyle"]![0]!.GetValue<string>());

    private static async Task<JsonObject> ProblemAsync(HttpResponseMessage response) =>
        (JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject)!;

    private async Task<HttpClient> LoginAsync(ModulixUser user, string? language = null)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (language is not null)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/account/language", new UpdateLanguageRequest(language))).StatusCode);
        }

        return client;
    }
}
