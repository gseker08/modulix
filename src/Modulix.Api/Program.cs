using Modulix.Api;
using Modulix.Api.Access;
using Modulix.Api.Account;
using Modulix.Api.Audit;
using Modulix.Api.Imports;
using Modulix.Api.Approvals;
using Modulix.Api.Notifications;
using Modulix.Api.Observability;
using Modulix.Api.Automation;
using Modulix.Api.Reports;
using Modulix.Api.Auth;
using Modulix.Api.Communication;
using Modulix.Api.Customization;
using Modulix.Api.Files;
using Modulix.Api.Views;
using Modulix.Api.Development;
using Modulix.Api.Email;
using Modulix.Api.Events;
using Modulix.Api.Lookups;
using Modulix.Api.Localization;
using Modulix.Api.Organization;
using Modulix.Api.Platform;
using Modulix.Api.Records;
using Modulix.Api.Settings;
using Modulix.Api.Numbering;
using Modulix.Api.Sectors;
using Modulix.Api.Tenancy;
using Modulix.Api.Users;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Customization;
using Modulix.Core.Modules;
using Modulix.Infrastructure.Email;
using Modulix.Infrastructure.Events;
using Modulix.Infrastructure.Persistence;
using Modulix.Infrastructure.Records;
using Modulix.Infrastructure.Users;

var builder = WebApplication.CreateBuilder(args);

// "migrate" komutu: bekleyen migration'ları uygular ve çıkar. Uygulama bu kullanıcıyla çalışmaz.
//   dotnet run --project src/Modulix.Api -- migrate
if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
{
    // Platform ve tenant zincirleri aynı veritabanına uygulanır (ADR-0020); modül tabloları modele girsin diye katalog verilir.
    var migrationsConnection = RequiredConnectionString(builder.Configuration, PersistenceSetup.MigrationsConnectionName);
    await PersistenceSetup.MigrateAsync(migrationsConnection, ModuleCatalog.Create(ModulixModules.Create()));
    Console.WriteLine("Migration'lar uygulandı.");
    return;
}

// Log satırlarında TraceId, tenant ve kullanıcı; OTLP adresi verilirse OpenTelemetry izleri ve metrikleri (A2).
builder.AddModulixObservability();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddModulixPersistence(
    RequiredConnectionString(builder.Configuration, PersistenceSetup.AppConnectionName),
    RequiredConnectionString(builder.Configuration, PersistenceSetup.MigrationsConnectionName));
// Özelleştirme sınırları (ADR-0041 K7): yapılandırma verilmezse sınırsız; sınır gerekirse "Customization:Limits" altına yazılır.
builder.Services.AddSingleton(builder.Configuration.GetSection("Customization:Limits").Get<Modulix.Core.Customization.CustomizationLimits>()
    ?? new Modulix.Core.Customization.CustomizationLimits());
builder.Services.AddModulixDataProtection(builder.Configuration);
builder.Services.AddModulixAuth();
builder.Services.AddModulixTenancy();
builder.Services.AddModulixServerLocalization();

builder.Services.AddModulixModules(ModulixModules.Create());
builder.Services.AddModulixModuleBridges();

// Sektör profilleri (Y9c): tanımlar açılışta modül kataloğuna göre doğrulanır.
builder.Services.AddSingleton(sp => new SectorProfileCatalog(SectorProfiles.Create(), sp.GetRequiredService<ModuleCatalog>()));
builder.Services.AddSingleton(sp => new SubProfileStarterCatalog(SubProfileStarters.Create(), sp.GetRequiredService<ModuleCatalog>()));

builder.Services.AddModulixRecords();
builder.Services.Configure<Modulix.Api.Communication.CommunicationCleanupOptions>(builder.Configuration.GetSection(Modulix.Api.Communication.CommunicationCleanupOptions.SectionName));
builder.Services.AddSingleton<Modulix.Api.Imports.ImportErrorReports>();
builder.Services.Configure<Modulix.Api.Imports.ImportReportCleanupOptions>(builder.Configuration.GetSection(Modulix.Api.Imports.ImportReportCleanupOptions.SectionName));
builder.Services.AddSingleton<Modulix.Api.Imports.ImportReportCleanup>();
builder.Services.AddHostedService<Modulix.Api.Imports.ImportReportCleanupWorker>();
builder.Services.AddSingleton<Modulix.Api.Imports.ImportRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Modulix.Api.Imports.ImportRunner>());
builder.Services.AddScoped<Modulix.Api.Imports.ImportContext>();
builder.Services.AddScoped<Modulix.Api.Imports.ImportRollback>();
builder.Services.AddSingleton<Modulix.Api.Communication.CommunicationCleanupRunner>();
builder.Services.AddHostedService<Modulix.Api.Communication.CommunicationCleanupWorker>();
builder.Services.Configure<Modulix.Api.Files.FileCleanupOptions>(builder.Configuration.GetSection(Modulix.Api.Files.FileCleanupOptions.SectionName));
builder.Services.AddSingleton<Modulix.Api.Files.FileCleanupRunner>();
builder.Services.AddHostedService<Modulix.Api.Files.FileCleanupWorker>();
builder.Services.Configure<Modulix.Api.Reports.ReportJobsOptions>(builder.Configuration.GetSection(Modulix.Api.Reports.ReportJobsOptions.SectionName));
builder.Services.AddSingleton<Modulix.Api.Reports.ReportScheduleRunner>();
builder.Services.AddHostedService<Modulix.Api.Reports.ReportScheduleWorker>();
builder.Services.Configure<Modulix.Infrastructure.Automation.AutomationOptions>(builder.Configuration.GetSection(Modulix.Infrastructure.Automation.AutomationOptions.SectionName));
builder.Services.Configure<Modulix.Infrastructure.Audit.AuditArchiveOptions>(builder.Configuration.GetSection(Modulix.Infrastructure.Audit.AuditArchiveOptions.SectionName));
builder.Services.Configure<Modulix.Core.Files.FileStorageOptions>(builder.Configuration.GetSection(Modulix.Core.Files.FileStorageOptions.SectionName));
builder.Services.AddHostedService<Modulix.Api.Automation.AutomationWorker>();
builder.Services.AddHostedService<ApprovalEscalationWorker>();

// E-posta: kuyruk + arka plan gönderici. Ayarlar "Email" bölümünde; SMTP şifresi user-secrets veya ortam değişkeninde.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddModulixEmail();
builder.Services.AddHostedService<EmailDispatchWorker>();

// Olaylar (ADR-0007): outbox'taki olaylar abonelere dağıtılır ve teslim edilir. Ayarlar "Events" bölümünde.
builder.Services.Configure<EventOptions>(builder.Configuration.GetSection(EventOptions.SectionName));
builder.Services.AddModulixEvents();
builder.Services.AddModulixHardening(builder.Configuration);
builder.Services.AddScoped<IPublicTenantScopes, Modulix.Api.PublicTenantScopes>();
builder.Services.AddCommunicationPublicRateLimit(builder.Configuration);
builder.Services.AddHostedService<EventDispatchWorker>();
builder.Services.Configure<Modulix.Api.Platform.PlatformOptions>(builder.Configuration.GetSection(Modulix.Api.Platform.PlatformOptions.SectionName));
builder.Services.Configure<Modulix.Infrastructure.Modules.LicenseOptions>(builder.Configuration.GetSection(Modulix.Infrastructure.Modules.LicenseOptions.SectionName));

// Modüllerin zamanlanmış işleri tek zamanlayıcıdan çalışır (ADR-0046); işler modül kaydından (IModule.Jobs) okunur.
builder.Services.AddSingleton<Modulix.Api.Jobs.ModuleJobRunner>();
builder.Services.AddHostedService<Modulix.Api.Jobs.ModuleJobsWorker>();

// Kullanıcı yönetimi: davet sınırı "Users" bölümünde; e-postadaki bağlantıların kökü App:PublicUrl (üretimde zorunlu).
builder.Services.Configure<UserAdministrationOptions>(builder.Configuration.GetSection(UserAdministrationOptions.SectionName));
builder.Services.Configure<PublicLinkOptions>(builder.Configuration.GetSection(PublicLinkOptions.SectionName));

// Hesap güvenliği (Y7a): "Şifremi unuttum" sınırları "Account" bölümünde.
builder.Services.Configure<AccountOptions>(builder.Configuration.GetSection(AccountOptions.SectionName));
// Sağlık uçları: /health/live yalnızca sürecin ayakta olduğunu söyler (yeniden başlatma kararı, bağımlılık denetlenmez);
// /health/ready trafik alabilir mi sorusudur (veritabanı, dosya deposu); /health bütün denetimleri (olay birikimi dahil) gösterir.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ModulixDbContext>("database", tags: [ReadyTag])
    .AddCheck<Modulix.Api.Files.BlobStorageHealthCheck>("storage", tags: [ReadyTag])
    .AddCheck<EventHealthCheck>("events");

var app = builder.Build();

// "seed" komutu: yalnızca geliştirme ortamında örnek tenant ve kullanıcıları oluşturur ve çıkar.
//   dotnet run --project src/Modulix.Api -- seed
if (args.Contains("seed", StringComparer.OrdinalIgnoreCase))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Örnek veri yalnızca Development ortamında oluşturulabilir.");
    }

    await DemoDataSeeder.SeedAsync(app.Services);
    Console.WriteLine($"Örnek veri hazır. Kullanıcılar: ayse@acme.demo, mehmet@beta.demo, deniz@ortak.demo, platform yöneticisi platform@modulix.demo (şifre: {DemoDataSeeder.DemoPassword})");
    return;
}

// "platform-admin" komutu: kişiyi platform yöneticisi yapar (--remove: kaldırır) ve çıkar. Arayüzden atanamaz.
//   dotnet run --project src/Modulix.Api -- platform-admin kisi@ornek.com
if (args.Contains(PlatformAdministratorCommand.Name, StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(await PlatformAdministratorCommand.RunAsync(app.Services, args));
    return;
}

// "tenant-export" / "tenant-delete" komutları: bir tenant'ın bütün verisini TenantId ile dışa aktarır ya da siler (ADR-0020 Dilim 3).
//   dotnet run --project src/Modulix.Api -- tenant-export acme [--dir exports/acme]
//   dotnet run --project src/Modulix.Api -- tenant-delete acme --confirm acme
if (TenantDataCommand.Matches(args))
{
    Console.WriteLine(await TenantDataCommand.RunAsync(app.Services, args));
    return;
}

// "audit-archive" komutu: sıcak saklama süresinden eski denetim kaydı aylarını nesne deposuna arşivler ve bölümü boşaltır (ADR-0052).
//   dotnet run --project src/Modulix.Api -- audit-archive
if (AuditArchiveCommand.Matches(args))
{
    var (archiveOutput, archived) = await AuditArchiveCommand.RunAsync(app.Services);
    Console.WriteLine(archiveOutput);
    if (!archived)
    {
        Environment.ExitCode = 1;
    }

    return;
}

// "license-apply" / "license-keygen" / "license-sign" komutları: on-prem imzalı lisans dosyası (ADR-0028 Dilim 7b).
//   dotnet run --project src/Modulix.Api -- license-apply lisans.json
if (LicenseCommand.Matches(args))
{
    Console.WriteLine(await LicenseCommand.RunAsync(app.Services, args));
    return;
}

// "test-email" komutu: verilen adrese deneme e-postası gönderir ve sonucu yazar; SMTP ayarlarını sınamak içindir.
//   dotnet run --project src/Modulix.Api -- test-email adres@ornek.com
if (args.Contains("test-email", StringComparer.OrdinalIgnoreCase))
{
    var to = args.SkipWhile(a => !a.Equals("test-email", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()
        ?? throw new InvalidOperationException("Alıcı adresi gerekli: -- test-email adres@ornek.com");
    Console.WriteLine(await TestEmail.SendAsync(app.Services, to));
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Ters vekil arkasında gerçek istemci IP'si (hız sınırı, denetim kaydı); yalnızca ForwardedHeaders:Enabled ile ve güvenilen vekillerden.
app.UseModulixForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    // İşlenmeyen hata gövdesiz 500 yerine ProblemDetails döner (ayrıntı sızdırmadan); geliştirmede hata sayfası kalır.
    app.UseExceptionHandler();
    app.UseHsts();
}

DataProtectionSetup.WarnIfKeysUnprotected(app);

// Gövdesiz API hata yanıtları (401, 403, 404, 405, 429…) da ProblemDetails taşır; arayüz sayfaları etkilenmez.
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), api => api.UseStatusCodePages());

app.UseHttpsRedirection();
app.UseModulixSecurityHeaders();

// Arayüz (Blazor WebAssembly) API ile aynı adresten sunulur; oturum çerezi SameSite=Strict kalabilir.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

// Hız sınırı kimlik doğrulamadan sonra çalışır: genel sınır oturumdaki kişi ve firmaya göre bölümlenir.
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<CompanyContextMiddleware>();
app.UseMiddleware<Modulix.Api.Observability.RequestLogScopeMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });
app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapTenantEndpoints();
app.MapAccessEndpoints();
app.MapUserEndpoints();
app.MapCompanyEndpoints();
app.MapTeamEndpoints();
app.MapBusinessUnitEndpoints();
app.MapAutomationEndpoints();
app.MapApprovalEndpoints();
app.MapNotificationEndpoints();
app.MapReportEndpoints();
app.MapCommunicationEndpoints();
app.MapCustomFieldEndpoints();
app.MapChoiceListEndpoints();
app.MapLookupEndpoints();
app.MapFormLayoutEndpoints();
app.MapGridViewEndpoints();
app.MapFileEndpoints();
app.MapSectorProfileEndpoints();
app.MapSubProfileEndpoints();
app.MapSectorEndpoints();
app.MapGtipCatalogEndpoints();
app.MapRegimeProfileEndpoints();
app.MapModuleEndpoints();
app.MapRuleEndpoints();
app.MapRecordEndpoints();
app.MapCommunicationPublicEndpoints();
app.MapPlatformEndpoints();
app.MapPlatformEventEndpoints();
app.MapPlatformPlanEndpoints();
app.MapPlatformHealthEndpoints();
app.MapSupportAccessEndpoints();
app.MapPlatformBillingEndpoints();
app.MapPlatformTeamEndpoints();
app.MapAuditEndpoints();
app.MapImportEndpoints();
app.MapSettingsEndpoints();
app.MapNumberingEndpoints();
app.MapTextEndpoints();

// Bilinmeyen API adresi 404 döner; diğer adresler arayüzün sayfalarıdır.
app.Map("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

static string RequiredConnectionString(IConfiguration configuration, string name) =>
    configuration.GetConnectionString(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException(
            $"ConnectionStrings:{name} bulunamadı. Önce scripts\\db-up.ps1 çalıştırın.");

/// <summary>Entegrasyon testleri (WebApplicationFactory) için.</summary>
public partial class Program
{
    /// <summary>Hazır olma denetimine (<c>/health/ready</c>) giren sağlık denetimlerinin etiketi.</summary>
    internal const string ReadyTag = "ready";
}
