using System.Globalization;
using System.Text;
using Modulix.Infrastructure.Audit;

namespace Modulix.Api.Platform;

/// <summary>
/// Denetim kaydı arşivi komutu (ADR-0052 dilim 2). Arayüzden çalıştırılamaz; işletmen ayda bir zamanlanmış görev olarak çalıştırır.
///   dotnet run --project src/Modulix.Api -- audit-archive
/// Sıcak saklama süresinden (<c>AuditArchive:HotMonths</c>) eski ayları tenant başına nesne deposuna yazar ve bölümü boşaltır; bölüm sınırlarını
/// bakımda tutar. Sonuç uygulama günlüğüne ve konsola yazılır. Sayı tutmayan dönem varsa komut hata koduyla biter.
/// </summary>
public static class AuditArchiveCommand
{
    public const string Name = "audit-archive";

    public static bool Matches(IReadOnlyList<string> args) => args.Contains(Name, StringComparer.OrdinalIgnoreCase);

    public static async Task<(string Output, bool Succeeded)> RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var archiver = scope.ServiceProvider.GetRequiredService<AuditArchiver>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var result = await archiver.RunAsync(now, CancellationToken.None);

        StringBuilder output = new();
        if (result.Periods.Count == 0)
        {
            output.AppendLine("Arşivlenecek dönem yok.");
        }

        foreach (var period in result.Periods)
        {
            var start = period.Start == DateTimeOffset.MinValue ? "başlangıç" : period.Start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (period.Cleared)
            {
                output.AppendLine(CultureInfo.InvariantCulture,
                    $"{start} – {period.End:yyyy-MM}: {period.Tenants} tenant, {period.Rows} satır arşivlendi; bölüm boşaltıldı.");
            }
            else
            {
                output.AppendLine(CultureInfo.InvariantCulture, $"{start} – {period.End:yyyy-MM}: {period.Problem}");
            }
        }

        output.Append(CultureInfo.InvariantCulture, $"Bölüm sınırı: {result.BoundariesMerged} kaldırıldı, {result.BoundariesAdded} eklendi.");
        return (output.ToString(), result.Periods.All(p => p.Cleared));
    }
}
