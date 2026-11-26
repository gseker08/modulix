using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulix.Api;
using Modulix.Api.Jobs;
using Modulix.Core.Contracts.Currency;
using Modulix.Core.Currency;

namespace Modulix.IntegrationTests;

/// <summary>
/// Merkez bankası kur işi (ADR-0049 dilim 5d): bülten firmanın kur defterine <c>CentralBank</c> kaynağıyla yazılır, tekrar çalışmak kur çoğaltmaz,
/// elle girilmiş kur ezilmez, kaynak kapalıyken hiçbir şey yazılmaz ve başka firmanın defteri etkilenmez. Dış ağ yoktur: bülten sahte beslemeden gelir.
/// </summary>
public class CentralBankRateJobTests : IClassFixture<ModulixApiFactory>, IAsyncLifetime
{
    private const string JobKey = "core.exchange-rates";

    private static readonly DateOnly Day = new(2026, 11, 26);

    private readonly ModulixApiFactory factory;
    private readonly WebApplicationFactory<Program> host;
    private readonly FakeFeed feed = new();

    public CentralBankRateJobTests(ModulixApiFactory factory)
    {
        this.factory = factory;
        host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICentralBankFeed>();
            services.AddSingleton<ICentralBankFeed>(feed);
        }));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task The_bulletin_is_written_once_updated_when_it_changes_and_never_over_a_manual_rate()
    {
        var tenant = await factory.CreateTenantAsync();
        feed.Snapshot = Bulletin(usd: 34.10m, eur: 37.50m);

        // Elle girilmiş EUR kuru aynı gün için varsa merkez bankası onu ezmez.
        await WriteManualAsync(tenant.Id, "EUR", 40m);

        var first = await RunAsync(tenant.Id);
        Assert.Equal((1, 0, 1, 1), (first.Created, first.Updated, first.Skipped, first.Missing)); // USD yeni; EUR elle; GBP bültende yok
        Assert.Equal(34.10m, (await FindAsync(tenant.Id, "USD"))!.Rate);
        Assert.Equal(40m, (await FindAsync(tenant.Id, "EUR"))!.Rate);

        // Aynı bülten tekrar: değişiklik yok.
        var again = await RunAsync(tenant.Id);
        Assert.Equal((0, 0, 2, 1), (again.Created, again.Updated, again.Skipped, again.Missing));

        // Aynı gün düzeltilmiş kur: merkez bankası kaynaklı kayıt güncellenir.
        feed.Snapshot = Bulletin(usd: 34.20m, eur: 37.50m);
        var corrected = await RunAsync(tenant.Id);
        Assert.Equal((0, 1, 1, 1), (corrected.Created, corrected.Updated, corrected.Skipped, corrected.Missing));
        Assert.Equal(34.20m, (await FindAsync(tenant.Id, "USD"))!.Rate);
    }

    [Fact]
    public async Task A_disabled_or_unreachable_source_writes_nothing_and_other_tenants_are_untouched()
    {
        var a = await SetUpAsync();
        var b = await SetUpAsync();
        feed.Snapshot = null;
        var none = await host.Services.GetRequiredService<ModuleJobRunner>().RunTenantAsync(JobKey, a, CancellationToken.None);
        Assert.Equal(ModuleJobRunStatus.Completed, none.Status);
        Assert.Null(await FindAsync(a, "USD"));

        // Yalnız çalıştırılan firmanın defterine yazılır.
        feed.Snapshot = Bulletin(usd: 34.10m, eur: 37.50m);
        await RunAsync(a);
        Assert.NotNull(await FindAsync(a, "USD"));
        Assert.Null(await FindAsync(b, "USD"));
    }

    private async Task<Guid> SetUpAsync() => (await factory.CreateTenantAsync()).Id;

    private async Task<CentralBankSyncResult> RunAsync(Guid tenantId)
    {
        var run = await host.Services.GetRequiredService<ModuleJobRunner>().RunTenantAsync(JobKey, tenantId, CancellationToken.None);
        Assert.Equal(ModuleJobRunStatus.Completed, run.Status);
        return (CentralBankSyncResult)run.Result!.Details!;
    }

    private async Task<ExchangeRateQuote?> FindAsync(Guid tenantId, string from)
    {
        using var tenantScope = factory.OpenScope(tenantId);
        return await tenantScope.ServiceProvider.GetRequiredService<IExchangeRateReader>().FindAsync(from, "TRY", Day, CancellationToken.None);
    }

    private async Task WriteManualAsync(Guid tenantId, string from, decimal rate)
    {
        using var scope = factory.OpenScope(tenantId);
        var pipeline = scope.ServiceProvider.GetRequiredService<Modulix.Core.Contracts.Records.IRecordPipeline>();
        var result = await pipeline.CreateAsync(new Modulix.Core.Contracts.Records.RecordCommand(CurrencyEntities.ExchangeRate, null, new System.Text.Json.Nodes.JsonObject
        {
            ["fromCurrency"] = from,
            ["toCurrency"] = "TRY",
            ["rate"] = rate,
            ["validOn"] = Day.ToString("yyyy-MM-dd"),
            ["source"] = ExchangeRateSources.Manual,
        }), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private static CentralBankSnapshot Bulletin(decimal usd, decimal eur) =>
        new(Day, [new("USD", usd), new("EUR", eur), new("JPY", 0.225m)]);

    private sealed class FakeFeed : ICentralBankFeed
    {
        public CentralBankSnapshot? Snapshot { get; set; }

        public Task<CentralBankSnapshot?> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);
    }
}
