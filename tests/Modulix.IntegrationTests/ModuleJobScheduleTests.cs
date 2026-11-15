using Microsoft.Extensions.Configuration;
using Modulix.Api.Jobs;
using Modulix.Core.Contracts.Modules;

namespace Modulix.IntegrationTests;

/// <summary>
/// ADR-0046: zamanlanmış işlerin yapılandırması tek kalıptır (<c>Jobs:&lt;anahtar&gt;</c>); işin eski bölümü yeni ayar verilmemişse okunur.
/// Veritabanı gerektirmez.
/// </summary>
public class ModuleJobScheduleTests
{
    private static readonly ModuleJobDefinition Job =
        new("orders.jobs", TimeSpan.FromMinutes(15), typeof(IModuleJob), "orders.scheduler") { LegacySection = "OrderJobs" };

    [Fact]
    public void Without_configuration_the_job_runs_at_its_default_interval()
    {
        var schedule = ModuleJobSchedule.Resolve(Configuration(), Job);

        Assert.True(schedule.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(15), schedule.Interval);
        Assert.True(ModuleJobSchedule.WorkerEnabled(Configuration()));
    }

    [Fact]
    public void The_job_section_sets_the_switch_and_the_interval()
    {
        var schedule = ModuleJobSchedule.Resolve(Configuration(("Jobs:orders.jobs:Enabled", "false"), ("Jobs:orders.jobs:IntervalSeconds", "120")), Job);

        Assert.False(schedule.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(120), schedule.Interval);
    }

    [Fact]
    public void The_legacy_section_is_still_read()
    {
        var schedule = ModuleJobSchedule.Resolve(Configuration(("OrderJobs:WorkerEnabled", "false"), ("OrderJobs:PollIntervalSeconds", "300")), Job);

        Assert.False(schedule.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(300), schedule.Interval);
    }

    [Fact]
    public void The_new_section_wins_over_the_legacy_one()
    {
        var schedule = ModuleJobSchedule.Resolve(
            Configuration(
                ("OrderJobs:WorkerEnabled", "false"), ("OrderJobs:PollIntervalSeconds", "300"),
                ("Jobs:orders.jobs:Enabled", "true"), ("Jobs:orders.jobs:IntervalSeconds", "60")),
            Job);

        Assert.True(schedule.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(60), schedule.Interval);
    }

    [Fact]
    public void The_interval_never_goes_below_the_minimum() =>
        Assert.Equal(ModuleJobSchedule.MinInterval, ModuleJobSchedule.Resolve(Configuration(("Jobs:orders.jobs:IntervalSeconds", "1")), Job).Interval);

    [Fact]
    public void A_job_without_a_legacy_section_ignores_old_names()
    {
        var schedule = ModuleJobSchedule.Resolve(Configuration(("OrderJobs:WorkerEnabled", "false")), Job with { LegacySection = null });

        Assert.True(schedule.Enabled);
    }

    [Fact]
    public void The_worker_switch_turns_the_whole_scheduler_off() =>
        Assert.False(ModuleJobSchedule.WorkerEnabled(Configuration(("Jobs:WorkerEnabled", "false"))));

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();
}
