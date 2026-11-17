using System.Text.RegularExpressions;

namespace Modulix.ArchitectureTests;

/// <summary>
/// Hız sınırı ilkeleri (ADR-0054): yapılandırmadaki dakikalık değerler küme toplamıdır ve her sunucu payını <c>RateLimits.PerNode</c> ile
/// alır (<c>RateLimiting:Nodes</c>). Bir ilke ayarı doğrudan <c>GetValue</c> ile okursa sunucu sayısı arttıkça sınır sessizce katlanır.
/// Bu test kaynak kodu okur: hız sınırı bölümü (<c>RateLimitPartition.Get…Limiter</c>) kuran her dosya <c>RateLimits.PerNode</c> çağırır
/// ve hiçbir dosya <c>…PerMinute</c> ayarını <c>GetValue</c> ile okumaz.
/// </summary>
public class RateLimitRulesTests
{
    private static readonly Regex LimiterExpression = new(@"RateLimitPartition\.Get(FixedWindow|SlidingWindow|TokenBucket|Concurrency)Limiter", RegexOptions.Compiled);
    private static readonly Regex DirectReadExpression = new(@"GetValue(<int>)?\(\s*\$?""[^""]*PerMinute""", RegexOptions.Compiled);

    [Fact]
    public void Every_rate_limit_policy_takes_its_permits_from_rate_limits_per_node()
    {
        var files = SourceFiles().ToList();
        var policies = files.Where(f => LimiterExpression.IsMatch(f.Text)).ToList();
        Assert.True(policies.Count >= 6, $"Hız sınırı ilkeleri bulunamadı ({policies.Count} dosya); test ayrıştırması bozulmuş olabilir.");

        var missing = policies.Where(f => !f.Text.Contains("RateLimits.PerNode(", StringComparison.Ordinal)).Select(f => f.Path).ToList();
        Assert.True(missing.Count == 0, "İzin sayısını RateLimits.PerNode ile almayan hız sınırı ilkesi:\n" + string.Join('\n', missing));

        var direct = files.Where(f => DirectReadExpression.IsMatch(f.Text)).Select(f => f.Path).ToList();
        Assert.True(direct.Count == 0, "Dakikalık sınır ayarını doğrudan okuyan dosya (RateLimits.PerNode kullanın):\n" + string.Join('\n', direct));
    }

    private static IEnumerable<(string Path, string Text)> SourceFiles()
    {
        var src = Path.Combine(RepositoryLayout.Root.FullName, "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RepositoryLayout.Root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (relative, File.ReadAllText(file));
        }
    }
}
