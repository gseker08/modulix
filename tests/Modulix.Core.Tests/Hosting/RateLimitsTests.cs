using Microsoft.Extensions.Configuration;
using Modulix.Core.Hosting;

namespace Modulix.Core.Tests.Hosting;

/// <summary>ADR-0054: dakikalık hız sınırı ayarı küme toplamıdır; her sunucu <c>RateLimiting:Nodes</c>'a bölünmüş payını alır.</summary>
public class RateLimitsTests
{
    private const string Key = "Auth:RateLimitPerMinute";

    [Fact]
    public void Single_node_keeps_the_configured_value_and_the_default_applies_when_unset()
    {
        Assert.Equal(30, RateLimits.PerNode(Configuration(), Key, 30));
        Assert.Equal(45, RateLimits.PerNode(Configuration((Key, "45")), Key, 30));
    }

    [Fact]
    public void Several_nodes_share_the_total_rounded_up()
    {
        var configuration = Configuration((Key, "100"), (RateLimits.NodesKey, "3"));

        Assert.Equal(34, RateLimits.PerNode(configuration, Key, 30));
    }

    [Fact]
    public void Share_never_drops_below_the_minimum_and_invalid_node_count_counts_as_one()
    {
        Assert.Equal(2, RateLimits.PerNode(Configuration((Key, "3"), (RateLimits.NodesKey, "4")), Key, 30, minimum: 2));
        Assert.Equal(2, RateLimits.PerNode(Configuration((Key, "-5")), Key, 30, minimum: 2));
        Assert.Equal(30, RateLimits.PerNode(Configuration((RateLimits.NodesKey, "0")), Key, 30));
    }

    [Fact]
    public void Zero_minimum_lets_a_disabled_limit_stay_disabled()
    {
        // Oturumlu genel sınırda 0 ya da negatif ayar sınırı kapatır; bölme bunu değiştirmez.
        Assert.Equal(0, RateLimits.PerNode(Configuration((Key, "0"), (RateLimits.NodesKey, "3")), Key, 600, minimum: 0));
        Assert.Equal(0, RateLimits.PerNode(Configuration((Key, "-1")), Key, 600, minimum: 0));
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
}
