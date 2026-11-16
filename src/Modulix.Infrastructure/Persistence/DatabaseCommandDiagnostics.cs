using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Modulix.Infrastructure.Persistence;

/// <summary>
/// Veritabanı komut izi ve süre metriği (A2, mimari değerlendirme). EF Core'un çalıştırdığı her komut için isteğin izine bağlı bir
/// istemci span'i (<see cref="ActivitySourceName"/>) ve <c>db.client.operation.duration</c> histogramı (<see cref="MeterName"/>) üretir;
/// adlar OpenTelemetry veritabanı semantik kurallarına uyar.
/// <para>
/// Kişisel veri sızmasın diye sorgu metni (<c>db.query.text</c>), parametre değerleri ve hata iletisi yazılmaz: yalnızca işlem türü
/// (SELECT, INSERT…), veritabanı adı, hata türü ve SQL Server hata numarası. Metrik etiketlerinde tenant yoktur (kardinalite).
/// </para>
/// <para>
/// Span, komut bittikten sonra EF'in ölçtüğü başlangıç ve süreyle geriye dönük oluşturulur; okuyucu döndüren sorguda süre ilk sonuca kadardır
/// (satırların okunması dahil değildir). Dinleyici yoksa (OpenTelemetry kapalı) komut metni hiç incelenmez. EF dışında doğrudan
/// <see cref="SqlConnection"/> ile çalışan komutlar (oturum kilitleri, RLS oturum değeri) bu izde yoktur.
/// </para>
/// </summary>
public sealed partial class DatabaseCommandDiagnostics : DbCommandInterceptor
{
    public const string ActivitySourceName = "Modulix.Database";
    public const string MeterName = "Modulix.Database";

    private const string SystemName = "microsoft.sql_server";
    private const int ScannedLength = 4096;

    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "db.client.operation.duration", unit: "s", description: "Veritabanı komutunun süresi (EF Core).");

    /// <summary>Durum tutmaz; bütün bağlamlar aynı örneği kullanır.</summary>
    public static DatabaseCommandDiagnostics Instance { get; } = new();

    private DatabaseCommandDiagnostics()
    {
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData, null);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData, null);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command, eventData, null);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData, null);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command, eventData, null);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData, null);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        Record(command, eventData, eventData.Exception);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Record(command, eventData, eventData.Exception);
        return Task.CompletedTask;
    }

    /// <summary>Komutun ilk işlem anahtar sözcüğü (SET/DECLARE önekleri atlanır); bulunamazsa <c>OTHER</c>.</summary>
    public static string OperationName(string? commandText)
    {
        if (string.IsNullOrEmpty(commandText))
        {
            return "OTHER";
        }

        Match match;
        try
        {
            match = OperationPattern().Match(commandText, 0, Math.Min(commandText.Length, ScannedLength));
        }
        catch (RegexMatchTimeoutException)
        {
            return "OTHER";
        }

        return !match.Success
            ? "OTHER"
            : match.Value.ToUpperInvariant() switch
            {
                "EXECUTE" => "EXEC",
                var keyword => keyword,
            };
    }

    private static void Record(DbCommand command, CommandEndEventData eventData, Exception? error)
    {
        var tracing = Source.HasListeners();
        if (!tracing && !Duration.Enabled)
        {
            return;
        }

        var operation = OperationName(command.CommandText);
        var errorType = error?.GetType().FullName;

        if (Duration.Enabled)
        {
            var tags = new TagList
            {
                { "db.system.name", SystemName },
                { "db.operation.name", operation },
            };
            if (errorType is not null)
            {
                tags.Add("error.type", errorType);
            }

            Duration.Record(eventData.Duration.TotalSeconds, tags);
        }

        if (!tracing)
        {
            return;
        }

        var database = command.Connection?.Database;
        using var activity = Source.StartActivity(
            string.IsNullOrEmpty(database) ? operation : $"{operation} {database}", ActivityKind.Client, parentContext: default,
            startTime: eventData.StartTime);
        if (activity is null)
        {
            return;
        }

        activity.SetTag("db.system.name", SystemName);
        activity.SetTag("db.operation.name", operation);
        if (!string.IsNullOrEmpty(database))
        {
            activity.SetTag("db.namespace", database);
        }

        if (errorType is not null)
        {
            activity.SetTag("error.type", errorType);
            if (error is SqlException sql)
            {
                activity.SetTag("db.response.status_code", sql.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            activity.SetStatus(ActivityStatusCode.Error);
        }

        activity.SetEndTime(eventData.StartTime.UtcDateTime + eventData.Duration);
    }

    [GeneratedRegex(@"\b(SELECT|INSERT|UPDATE|DELETE|MERGE|EXECUTE|EXEC)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex OperationPattern();
}
