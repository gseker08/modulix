using System.Net;
using System.Net.Http.Json;
using Modulix.Core.Approvals;
using Modulix.Core.Contracts.Calendar;
using Modulix.Core.Modules;
using Modulix.Core.Platform;
using Modulix.Infrastructure.Identity;
using Modulix.Shared.Approvals;
using Modulix.Shared.Auth;
using Modulix.Shared.Settings;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0049 dilim 6d: süreç seçeneğiyle onay adımı süresi firmanın iş takvimiyle sayılır. Mutlu yol, doğrulama, tenant izolasyonu, yetki.
/// </summary>
public class ApprovalWorkingHoursTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    private sealed record Team(Tenant Tenant, ModulixUser Admin, ModulixUser Requester, ModulixUser Approver);

    [Fact]
    public async Task Working_hours_process_counts_the_step_time_in_the_company_calendar()
    {
        Team team = await SetUpAsync();
        using HttpClient admin = await LoginAsync(team.Admin);
        using HttpClient requester = await LoginAsync(team.Requester);

        ApprovalDefinitionDto working = await CreateDefinitionAsync(admin, team, dueInWorkingHours: true);
        ApprovalDefinitionDto wall = await CreateDefinitionAsync(admin, team, dueInWorkingHours: false);
        Assert.True(working.DueInWorkingHours);
        Assert.False(wall.DueInWorkingHours);

        ApprovalStepDto wallStep = (await OpenAsync(requester, wall)).Steps[0];
        Assert.Equal(wallStep.ActivatedAt!.Value.AddHours(24), wallStep.DueAt);

        ApprovalStepDto workingStep = (await OpenAsync(requester, working)).Steps[0];
        Assert.Equal(await ExpectedDueAsync(admin, workingStep.ActivatedAt!.Value), workingStep.DueAt);

        // 24 saatlik mesai en az üç iş gününe yayılır: takvim saatinden belirgin biçimde uzun.
        Assert.True(workingStep.DueAt - workingStep.ActivatedAt > TimeSpan.FromHours(48));
    }

    [Fact]
    public async Task Another_tenants_work_calendar_does_not_change_the_deadline()
    {
        Team a = await SetUpAsync();
        Team b = await SetUpAsync();
        using HttpClient adminA = await LoginAsync(a.Admin);
        using HttpClient adminB = await LoginAsync(b.Admin);
        using HttpClient requesterA = await LoginAsync(a.Requester);

        await SetSettingAsync(adminB, SettingKeys.WorkDays, WorkWeek.SundayToThursday);
        await SetSettingAsync(adminB, SettingKeys.WorkHours, "08:00-17:00");

        ApprovalStepDto step = (await OpenAsync(requesterA, await CreateDefinitionAsync(adminA, a, dueInWorkingHours: true))).Steps[0];

        Assert.Equal(await ExpectedDueAsync(adminA, step.ActivatedAt!.Value), step.DueAt);
        Assert.Empty(await GetAsync<List<ApprovalDefinitionDto>>(adminB, "/api/approvals/definitions"));
    }

    [Fact]
    public async Task Working_hours_step_time_is_limited_and_only_admins_can_set_the_option()
    {
        Team team = await SetUpAsync();
        using HttpClient admin = await LoginAsync(team.Admin);
        using HttpClient requester = await LoginAsync(team.Requester);

        HttpResponseMessage tooLong = await admin.PostAsJsonAsync("/api/approvals/definitions", Request(team, ApprovalDefinition.MaxWorkingDueHours + 1, true));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);

        HttpResponseMessage forbidden = await requester.PostAsJsonAsync("/api/approvals/definitions", Request(team, 24, true));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Seçenek sonradan açılınca süreç yeni sürüme geçer; açılmış talep kendi ayarıyla sürer.
        ApprovalDefinitionDto wall = await CreateDefinitionAsync(admin, team, dueInWorkingHours: false);
        ApprovalRequestDto opened = await OpenAsync(requester, wall);
        ApprovalDefinitionDto updated = await ReadAsync<ApprovalDefinitionDto>(await admin.PutAsJsonAsync(
            $"/api/approvals/definitions/{wall.Id}", Request(team, 24, true) with { Name = wall.Name, ExpectedVersion = wall.Version }), HttpStatusCode.OK);
        Assert.True(updated.DueInWorkingHours);
        Assert.Equal(wall.Revision + 1, updated.Revision);

        ApprovalRequestDto unchanged = await GetAsync<ApprovalRequestDto>(requester, $"/api/approvals/requests/{opened.Id}");
        Assert.Equal(opened.Steps[0].DueAt, unchanged.Steps[0].DueAt);
    }

    // Firmanın ayarlarından beklenen bitiş: yeni firmada tatil yok, saat dilimi ve mesai ayarlardan.
    private static async Task<DateTimeOffset> ExpectedDueAsync(HttpClient admin, DateTimeOffset activatedAt)
    {
        SettingsResponse settings = await GetAsync<SettingsResponse>(admin, "/api/settings");
        string Value(string key) => settings.Settings.First(s => s.Key == key).Value;
        CalendarSpec spec = new(Value(SettingKeys.TimeZone), false, WorkWeek.Weekly(Value(SettingKeys.WorkDays), Value(SettingKeys.WorkHours)), []);
        return new WorkCalendar(spec).AddWorkingTime(activatedAt, TimeSpan.FromHours(24));
    }

    private static async Task SetSettingAsync(HttpClient client, string key, string value)
    {
        SettingDto current = (await GetAsync<SettingsResponse>(client, "/api/settings")).Settings.First(x => x.Key == key);
        HttpResponseMessage response = await client.PutAsJsonAsync("/api/settings",
            new SaveSettingsRequest(new Dictionary<string, SettingWriteDto> { [key] = new(value, current.Version) }));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<Team> SetUpAsync()
    {
        Tenant tenant = await factory.CreateTenantAsync();
        ModulixUser admin = await factory.CreateUserAsync(tenant);
        ModulixUser requester = await factory.CreateUserAsync(tenant);
        ModulixUser approver = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, admin);
        await factory.CreateRoleAsync(tenant.Id, [CoreModule.Permissions.RequestApprovals], requester);
        await factory.CreateRoleAsync(tenant.Id, [CoreModule.Permissions.DecideApprovals], approver);
        return new Team(tenant, admin, requester, approver);
    }

    private static SaveApprovalDefinitionRequest Request(Team team, int dueHours, bool dueInWorkingHours) =>
        new($"Satın alma {Guid.NewGuid():N}"[..24], null, null, [new ApprovalStepDefinitionDto("Yönetici", 1, team.Approver.Id, null, null, dueHours)],
            DueInWorkingHours: dueInWorkingHours);

    private static async Task<ApprovalDefinitionDto> CreateDefinitionAsync(HttpClient admin, Team team, bool dueInWorkingHours) =>
        await ReadAsync<ApprovalDefinitionDto>(await admin.PostAsJsonAsync("/api/approvals/definitions", Request(team, 24, dueInWorkingHours)), HttpStatusCode.Created);

    private static async Task<ApprovalRequestDto> OpenAsync(HttpClient client, ApprovalDefinitionDto definition) =>
        await ReadAsync<ApprovalRequestDto>(await client.PostAsJsonAsync(
            "/api/approvals/requests", new OpenApprovalRequest(definition.Id, "Yazıcı alımı", null, null, null)), HttpStatusCode.Created);

    private async Task<HttpClient> LoginAsync(ModulixUser user)
    {
        HttpClient client = factory.CreateBrowserClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email!, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url) => await ReadAsync<T>(await client.GetAsync(url), HttpStatusCode.OK);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
