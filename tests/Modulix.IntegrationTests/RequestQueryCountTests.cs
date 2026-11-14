using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Infrastructure.Persistence;
using Modulix.Shared.Auth;
using Modulix.Shared.Crm;
using Xunit.Abstractions;

namespace Modulix.IntegrationTests;

/// <summary>
/// Oturumlu bir API isteğinin veritabanı tur sayısı: oturum, tenant, şirket ve yetki denetimleri uç noktanın kendi işinden önce
/// her istekte çalışır; sayı büyürse bütün ekranlar yavaşlar. Sayaç ayrı bir API örneğinde çalışır, başka testlerin istekleri sayılmaz.
/// </summary>
public class RequestQueryCountTests(ModulixApiFactory factory, ITestOutputHelper output) : IClassFixture<ModulixApiFactory>
{
    [Fact]
    public async Task Listing_companies_stays_within_the_query_budget()
    {
        var tenant = await factory.CreateTenantAsync();
        await factory.SetModuleAsync(tenant.Id, "crm", enabled: true);
        var admin = await factory.CreateUserAsync(tenant);
        await factory.MakeAdministratorAsync(tenant.Id, admin);

        var counter = new CommandCounter();
        using var counted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ModulixDbContext>(options => options.AddInterceptors(counter, counter.Connections))));
        using var client = counted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(admin.Email!, ModulixApiFactory.Password))).StatusCode);

        // İlk istek önbellekleri ısıtır; ölçülen ikinci istektir.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/crm/companies")).StatusCode);
        counter.Reset();
        var response = await client.GetAsync("/api/crm/companies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<CrmPage<CompanyDto>>());
        output.WriteLine($"Komut: {counter.Commands}, bağlantı açılışı: {counter.Connections.Opened}");

        // 14.11.2026 ölçümü (ADR-0048): önce 17 komut ve 17 bağlantı açılışı (her açılış ayrıca sp_set_session_context turu);
        // sonra 4 komut (oturum denetimi + uç noktanın 3 sorgusu) ve 2 açılış (oturum denetimi + tenant kesinleşince tutulan bağlantı).
        var queries = string.Join(Environment.NewLine + "---" + Environment.NewLine, counter.Texts);
        // Bir pay: oturumun son görülme zamanı beş dakikada bir güncellenir (UserSessions.TouchInterval), yavaş makinede araya girebilir.
        Assert.True(counter.Commands <= 5, $"Komut sayısı {counter.Commands}:{Environment.NewLine}{queries}");
        Assert.True(counter.Connections.Opened <= 2, $"Bağlantı açılışı {counter.Connections.Opened}");
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int commands;

        public ConnectionCounter Connections { get; } = new();

        public int Commands => commands;

        public System.Collections.Concurrent.ConcurrentQueue<string> Texts { get; private set; } = new();

        public void Reset()
        {
            Interlocked.Exchange(ref commands, 0);
            Connections.Reset();
            Texts = new();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Count(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Count(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Count(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        private void Count(DbCommand command)
        {
            Interlocked.Increment(ref commands);
            Texts.Enqueue(command.CommandText);
        }
    }

    private sealed class ConnectionCounter : DbConnectionInterceptor
    {
        private int opened;

        public int Opened => opened;

        public void Reset() => Interlocked.Exchange(ref opened, 0);

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref opened);
            return Task.CompletedTask;
        }

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Interlocked.Increment(ref opened);
    }
}
