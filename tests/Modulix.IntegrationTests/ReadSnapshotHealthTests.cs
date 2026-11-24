using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulix.Api.Observability;

namespace Modulix.IntegrationTests;

/// <summary>
/// /health okuma anlık görüntüsü ayarını gösterir (ADR-0058). Paylaşılan test veritabanında ayarı gerçekten kapatmak
/// diğer testlerin bağlantılarını koparacağından kapalı durum probe'u değiştirilerek taklit edilir; gerçek sorgu açık durumda sınanır.
/// Uç oturumsuz bir altyapı ucudur: tenant verisi okumaz, bu yüzden tenant izolasyonu ve yetki testi yoktur.
/// </summary>
public class ReadSnapshotHealthTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    [Fact]
    public async Task The_real_probe_sees_the_setting_that_init_sql_turns_on()
    {
        var probe = factory.Services.GetRequiredService<IReadSnapshotProbe>();

        Assert.True(await probe.IsEnabledAsync(CancellationToken.None), "READ_COMMITTED_SNAPSHOT kapalı: docker/sqlserver/init.sql'i yeniden çalıştırın.");
    }

    [Fact]
    public async Task Health_is_degraded_when_the_setting_is_off_but_readiness_is_not_affected()
    {
        using var host = WithProbe(new FixedProbe(false));
        using var client = host.CreateClient();

        var health = await client.GetAsync("/health");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Degraded", await health.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains("Healthy", await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_is_degraded_not_unhealthy_when_the_setting_cannot_be_read()
    {
        using var host = WithProbe(new ThrowingProbe());
        using var client = host.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_response_carries_only_the_status_so_the_unauthenticated_endpoint_leaks_nothing()
    {
        using var host = WithProbe(new FixedProbe(false));
        using var client = host.CreateClient();

        var body = await (await client.GetAsync("/health")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("READ_COMMITTED_SNAPSHOT", body);
        Assert.DoesNotContain("tenant-isletim", body);
        Assert.DoesNotContain("Veritabanı", body);
    }

    private WebApplicationFactory<Program> WithProbe(IReadSnapshotProbe probe) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReadSnapshotProbe>();
            services.AddSingleton(probe);
        }));

    private sealed class FixedProbe(bool enabled) : IReadSnapshotProbe
    {
        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(enabled);
    }

    private sealed class ThrowingProbe : IReadSnapshotProbe
    {
        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("okunamadı");
    }
}
