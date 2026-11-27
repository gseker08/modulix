using Modulix.Core.Contracts.Events;
using Modulix.Core.Contracts.Modules;
using Modulix.Core.Modules;

namespace Modulix.Core.Tests.Modules;

/// <summary>ADR-0046: modülün zamanlanmış iş beyanı açılışta doğrulanır; host'taki tek zamanlayıcı işleri katalogdan okur.</summary>
public class ModuleJobCatalogTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    [Fact]
    public void The_catalog_lists_declared_jobs_with_their_module()
    {
        var catalog = new ModuleCatalog(
        [
            new CoreModule(),
            new JobModule("sales", ModuleJobDefinition.For<DemoJob>("sales.reminders", Interval, "sales.scheduler") with { LegacySection = "SalesJobs" }),
        ]);

        // Core da kendi işini bildirir (merkez bankası kur çekimi, ADR-0049 dilim 5d); satış işi onunla birlikte listelenir.
        Assert.Contains(catalog.Jobs, j => j.ModuleKey == "core" && j.Job.Key == "core.exchange-rates");
        var (moduleKey, job) = Assert.Single(catalog.Jobs, j => j.ModuleKey == "sales");
        Assert.Equal("sales", moduleKey);
        Assert.Equal("sales.reminders", job.Key);
        Assert.True(job.RequiresWritableModule);
        Assert.Equal("SalesJobs", catalog.FindJob("sales.reminders")!.Value.Job.LegacySection);
        Assert.Null(catalog.FindJob("sales.other"));
    }

    [Fact]
    public void A_job_key_must_start_with_the_module_key() =>
        Rejected(ModuleJobDefinition.For<DemoJob>("billing.reminders", Interval, "sales.scheduler"));

    [Fact]
    public void A_job_key_follows_the_subscription_key_format() =>
        Rejected(ModuleJobDefinition.For<DemoJob>("sales.Reminders", Interval, "sales.scheduler"));

    [Fact]
    public void A_job_declared_twice_is_rejected() =>
        Rejected(
            ModuleJobDefinition.For<DemoJob>("sales.reminders", Interval, "sales.scheduler"),
            ModuleJobDefinition.For<DemoJob>("sales.reminders", Interval, "sales.scheduler"));

    [Fact]
    public void A_job_needs_a_positive_interval() =>
        Rejected(ModuleJobDefinition.For<DemoJob>("sales.reminders", TimeSpan.Zero, "sales.scheduler"));

    [Fact]
    public void A_queue_job_declares_a_busy_interval_shorter_than_its_interval()
    {
        var catalog = new ModuleCatalog(
            [new CoreModule(), new JobModule("sales", ModuleJobDefinition.For<DemoJob>("sales.send", Interval, "sales.scheduler") with { BusyInterval = TimeSpan.FromSeconds(2) })]);

        Assert.Equal(TimeSpan.FromSeconds(2), catalog.FindJob("sales.send")!.Value.Job.BusyInterval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(900)]
    [InlineData(1200)]
    public void A_busy_interval_that_is_not_shorter_than_the_interval_is_rejected(int seconds) =>
        Rejected(ModuleJobDefinition.For<DemoJob>("sales.send", Interval, "sales.scheduler") with { BusyInterval = TimeSpan.FromSeconds(seconds) });

    [Fact]
    public void The_job_context_reads_positive_integer_settings_and_falls_back_otherwise()
    {
        var context = new ModuleJobContext(Guid.NewGuid())
        {
            Settings = new Dictionary<string, string?> { ["BatchSize"] = "250", ["Zero"] = "0", ["Text"] = "çok", ["Empty"] = null },
        };

        Assert.Equal(250, context.PositiveInt("BatchSize", 100));
        Assert.Equal(100, context.PositiveInt("Zero", 100));
        Assert.Equal(100, context.PositiveInt("Text", 100));
        Assert.Equal(100, context.PositiveInt("Empty", 100));
        Assert.Equal(100, context.PositiveInt("Missing", 100));
        Assert.Equal(7, new ModuleJobContext(Guid.NewGuid()).PositiveInt("BatchSize", 7));
    }

    [Fact]
    public void A_job_runner_must_implement_the_job_interface() =>
        Rejected(new ModuleJobDefinition("sales.reminders", Interval, typeof(string), "sales.scheduler"));

    [Fact]
    public void A_job_identity_must_be_a_subscription_of_the_same_module() =>
        Rejected(ModuleJobDefinition.For<DemoJob>("sales.reminders", Interval, "core.assignment-email"));

    private static void Rejected(params ModuleJobDefinition[] jobs) =>
        Assert.Throws<InvalidOperationException>(() => new ModuleCatalog([new CoreModule(), new JobModule("sales", jobs)]));

    private sealed class JobModule(string key, params ModuleJobDefinition[] jobs) : IModule
    {
        public string Key => key;

        public string DisplayName => key;

        public IReadOnlyList<PermissionDefinition> Permissions => [];

        public IReadOnlyList<EventSubscription> EventSubscriptions { get; } =
            [EventSubscription.For<Anchor>($"{key}.scheduler", [DomainEventTypes.RecordDeleted], [], [])];

        public IReadOnlyList<ModuleJobDefinition> Jobs => jobs;
    }

    private sealed class Anchor : IEventSubscriber
    {
        public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DemoJob : IModuleJob
    {
        public Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken) => Task.FromResult(ModuleJobResult.Nothing);
    }
}
