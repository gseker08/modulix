using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Numbering;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Modules;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Numbering;

/// <summary>
/// Ortak belge numaralama (ADR-0049). Sayaç tek SQL yığınıyla artar: satır yoksa açılır (aralık kilidiyle, iki istek aynı anda açamaz), sonra
/// <c>UPDATE ... OUTPUT</c> ile bir artırılıp okunur. Güncelleme satırı işlem sonuna dek kilitler; isteğin açık işlemi (kayıt boru hattı) içinde
/// çalıştığı için numara kayıtla birlikte kesinleşir ya da geri alınır. Ham SQL tenant süzgecini açıkça taşır; RLS ikinci katmandır.
/// </summary>
internal sealed class DocumentNumbers(ModulixDbContext db, ModuleCatalog modules, ITenantContextAccessor tenantAccessor, IServiceProvider services) : IDocumentNumbers
{
    // Firma saati çağrı anında çözülür: saat → ayarlar → kayıt boru hattı → kayıt depoları → bu servis döngüsü kurucuda kurulamaz.
    private ITenantClock Clock => services.GetRequiredService<ITenantClock>();

    private const string NextSql = """
        INSERT INTO core.NumberCounters (Id, TenantId, SeriesKey, Period, LastValue, UpdatedAt)
        SELECT @id, @tenant, @series, @period, @seed, SYSDATETIMEOFFSET()
        WHERE NOT EXISTS (
            SELECT 1 FROM core.NumberCounters WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId = @tenant AND SeriesKey = @series AND Period = @period);

        UPDATE core.NumberCounters
        SET LastValue = LastValue + 1, UpdatedAt = SYSDATETIMEOFFSET()
        OUTPUT inserted.LastValue
        WHERE TenantId = @tenant AND SeriesKey = @series AND Period = @period;
        """;

    public async Task<string> NextAsync(string seriesKey, Func<int, CancellationToken, Task<long>>? seedLastValue = null, CancellationToken cancellationToken = default)
    {
        var series = modules.FindNumberSeries(seriesKey)
            ?? throw new InvalidOperationException($"Belge serisi beyan edilmemiş: '{seriesKey}'. Modül seriyi IModule.NumberSeries ile bildirmelidir.");
        var tenantId = tenantAccessor.Current?.TenantId
            ?? throw new InvalidOperationException("Belge numarası tenant bağlamı olmadan verilemez.");

        var year = (await Clock.GetAsync(cancellationToken)).Today.Year;
        var period = series.PeriodOf(year);

        // Tohum yalnızca sayaç henüz yokken sorulur; iki istek aynı anda sorsa bile satırı yalnızca biri açar.
        long seed = 0;
        if (seedLastValue is not null
            && !await db.NumberCounters.AsNoTracking().AnyAsync(c => c.SeriesKey == seriesKey && c.Period == period, cancellationToken))
        {
            seed = Math.Max(0, await seedLastValue(year, cancellationToken));
        }

        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            // Bağlantıyı EF açar: tenant oturum bağlamı (RLS) açılışta kurulur.
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = NextSql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = Guid.CreateVersion7() });
            command.Parameters.Add(new SqlParameter("@tenant", SqlDbType.UniqueIdentifier) { Value = tenantId });
            command.Parameters.Add(new SqlParameter("@series", SqlDbType.NVarChar, NumberSeriesDefinition.KeyMaxLength) { Value = seriesKey });
            command.Parameters.Add(new SqlParameter("@period", SqlDbType.Int) { Value = period });
            command.Parameters.Add(new SqlParameter("@seed", SqlDbType.BigInt) { Value = seed });

            var value = await command.ExecuteScalarAsync(cancellationToken) as long?
                ?? throw new InvalidOperationException($"Belge serisi sayacı okunamadı: '{seriesKey}'.");
            return series.Format(value, year);
        }
        finally
        {
            if (opened)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
