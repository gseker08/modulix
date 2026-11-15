using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulix.Api.Observability;
using Modulix.Core.Contracts.Notifications;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Infrastructure.Email;
using Modulix.Infrastructure.Platform;
using Modulix.Shared.Auth;
using OpenTelemetry.Trace;

namespace Modulix.IntegrationTests;

/// <summary>Gözlemlenebilirlik (A2): log kapsamında tenant, kullanıcı ve iz kimliği; OpenTelemetry yalnızca adres verilince.</summary>
public class ObservabilityTests(ModulixApiFactory factory) : IClassFixture<ModulixApiFactory>
{
    [Fact]
    public async Task Logs_of_a_signed_in_request_carry_tenant_user_and_trace()
    {
        var logs = new CapturingLoggerProvider();
        using var host = WithLogs(logs);
        var tenant = await factory.CreateTenantAsync();
        var user = await factory.CreateUserAsync(tenant);
        using var client = await LoginAsync(host, user.Email!);

        var response = await client.GetAsync("/api/tenant/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // "Executing endpoint" ve "Executed endpoint" satırları; ikisi de kapsamın içindedir.
        var entry = logs.Entries.FirstOrDefault(e =>
            e.Category.StartsWith("Microsoft.AspNetCore.Routing", StringComparison.Ordinal)
            && e.Message.Contains("/api/tenant/current", StringComparison.Ordinal));
        Assert.NotNull(entry);
        Assert.Equal(tenant.Id, entry.Scope[RequestLogScopeMiddleware.TenantIdKey]);
        Assert.Equal(user.Id, entry.Scope[RequestLogScopeMiddleware.UserIdKey]);
        Assert.True(entry.Scope.ContainsKey(RequestLogScopeMiddleware.MembershipIdKey));
        Assert.True(entry.Scope.ContainsKey("TraceId"));
    }

    [Fact]
    public async Task Anonymous_requests_get_no_tenant_scope()
    {
        var logs = new CapturingLoggerProvider();
        using var host = WithLogs(logs);
        using var client = host.CreateClient();

        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest($"yok-{Guid.NewGuid():N}@test.modulix", "Yanlis.Sifre.2026"));

        Assert.DoesNotContain(logs.Entries, e => e.Scope.ContainsKey(RequestLogScopeMiddleware.TenantIdKey) || e.Scope.ContainsKey(RequestLogScopeMiddleware.UserIdKey));
    }

    [Fact]
    public void OpenTelemetry_is_off_without_an_endpoint()
    {
        Assert.Null(factory.Services.GetService<TracerProvider>());
    }

    [Fact]
    public void OpenTelemetry_is_on_with_an_endpoint()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("OpenTelemetry:OtlpEndpoint", "http://localhost:4317"));

        Assert.NotNull(host.Services.GetService<TracerProvider>());
    }

    [Fact]
    public async Task Logs_inside_a_tenant_scope_carry_the_tenant_until_the_scope_ends()
    {
        var logs = new CapturingLoggerProvider();
        using var host = WithLogs(logs);
        var tenantId = Guid.NewGuid();
        var factoryOfScopes = host.Services.GetRequiredService<ITenantScopeFactory>();
        var logger = host.Services.GetRequiredService<ILogger<ObservabilityTests>>();

        await using (factoryOfScopes.Create(new TenantContext(tenantId, Guid.Empty, Guid.Empty)))
        {
            logger.LogInformation("kapsam-ici");
        }

        logger.LogInformation("kapsam-disi");

        var inside = Assert.Single(logs.Entries, e => e.Message == "kapsam-ici");
        Assert.Equal(tenantId, inside.Scope[RequestLogScopeMiddleware.TenantIdKey]);
        Assert.False(inside.Scope.ContainsKey(RequestLogScopeMiddleware.UserIdKey)); // sistem işi: kullanıcı yok
        var outside = Assert.Single(logs.Entries, e => e.Message == "kapsam-disi");
        Assert.False(outside.Scope.ContainsKey(RequestLogScopeMiddleware.TenantIdKey));
    }

    [Fact]
    public void Job_turns_are_measured_per_job_without_tenant_tags()
    {
        var metrics = factory.Services.GetRequiredService<JobMetrics>();
        var job = $"test.{Guid.NewGuid():N}";
        var ages = new ConcurrentDictionary<string, double>();
        var turns = new ConcurrentBag<(string Outcome, IReadOnlyDictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == JobMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            var values = ToDictionary(tags);
            if (instrument.Name == "modulix.jobs.last_completed_turn.age" && values["job"] is string name)
            {
                ages[name] = value;
            }
        });
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var values = ToDictionary(tags);
            if (instrument.Name == "modulix.jobs.turns" && Equals(values["job"], job))
            {
                turns.Add(((string)values["outcome"]!, values));
            }
        });
        listener.Start();

        metrics.TurnCompleted(job);
        metrics.TurnFailed(job);
        listener.RecordObservableInstruments();

        Assert.True(ages.TryGetValue(job, out var age) && age >= 0);
        Assert.Contains(turns, t => t.Outcome == "completed");
        Assert.Contains(turns, t => t.Outcome == "failed");
        Assert.All(turns, t => Assert.Equal(new[] { "job", "outcome" }, t.Tags.Keys.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task Email_queue_is_measured_even_without_email_settings()
    {
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IEmailQueue>().Enqueue(new EmailMessage($"o-{Guid.NewGuid():N}@test.modulix", "Ölçüm", "<p>Ölçüm</p>"));
        }

        using var measure = factory.Services.CreateScope();
        var snapshot = await measure.ServiceProvider.GetRequiredService<EmailDispatcher>().MeasureAsync(CancellationToken.None);

        Assert.True(snapshot.Pending >= 1);
        Assert.NotNull(snapshot.OldestPending);
        Assert.True(snapshot.OldestPendingAge >= TimeSpan.Zero);
    }

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            values[tag.Key] = tag.Value;
        }

        return values;
    }

    // Uç nokta yürütme logları (Microsoft.AspNetCore.Routing, Information) kapsamın içinde yazılır; varsayılanda Warning'dir.
    private WebApplicationFactory<Program> WithLogs(CapturingLoggerProvider logs) =>
        factory.WithWebHostBuilder(builder => builder
            .UseSetting("Logging:LogLevel:Microsoft.AspNetCore.Routing", "Information")
            .ConfigureLogging(logging => logging.AddProvider(logs)));

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> host, string email)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, ModulixApiFactory.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private sealed record LogEntry(string Category, string Message, IReadOnlyDictionary<string, object?> Scope);

    /// <summary>Yazılan her log satırını, o andaki kapsam değerleriyle birlikte saklar.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => provider.scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = new Dictionary<string, object?>();
                provider.scopes.ForEachScope(
                    (scope, target) =>
                    {
                        if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                        {
                            foreach (var pair in pairs)
                            {
                                target[pair.Key] = pair.Value;
                            }
                        }
                    },
                    values);
                provider.Entries.Enqueue(new LogEntry(category, formatter(state, exception), values));
            }
        }
    }
}
