using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Reporting;
using Modulix.Core.Reporting;

namespace Modulix.Core.Tests.Reporting;

/// <summary>Modülün hazır tablosunun dışa aktarılması (ADR-0046): tablo rapor sonucuna çevrilir, rapor hataları kayıt hatasına döner.</summary>
public class ReportTableExportsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    private static readonly ReportTable Table = new(
        "Satış kartı - Gerçekleşen - Kasım", "crm.opportunities", "Gerçekleşen",
        [new ReportTableColumn("number", "Fırsat no", DatasetColumnKind.Text), new ReportTableColumn("amount", "Tutar", DatasetColumnKind.Number)],
        [["F-1", 100m], ["F-2", 250m]], Truncated: true, Now);

    [Fact]
    public async Task The_table_becomes_a_single_page_result_and_the_file_info_comes_back()
    {
        var exports = new FakeExports();

        var result = await new ReportTableExports(exports).CreateAsync(Table, ReportTableFormat.Xlsx, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(("Satış kartı - Gerçekleşen - Kasım", "crm.opportunities", ReportFileFormat.Xlsx), (exports.Name, exports.DatasetKey, exports.Format));
        var sent = exports.Result!;
        Assert.Equal(["number", "amount"], sent.Columns.Select(c => c.Key));
        Assert.All(sent.Columns, c => Assert.Equal(ReportColumnRole.Value, c.Role));
        Assert.Equal([["F-1", 100m], ["F-2", 250m]], sent.Rows.Select(r => r.Cells.ToArray()));
        Assert.Equal((false, 2, 2, true, "Gerçekleşen", Now), (sent.IsSummary, sent.RowsRead, sent.RowsMatched, sent.Truncated, sent.DatasetName, sent.GeneratedAt));
        Assert.Equal(("manual", 2, "Satış kartı.xlsx"), (result.Value!.Source, result.Value.RowCount, result.Value.FileName));
    }

    [Fact]
    public async Task Csv_is_the_default_format() =>
        Assert.Equal(ReportFileFormat.Csv, await FormatOf(ReportTableFormat.Csv));

    [Theory]
    [InlineData("rule", RecordErrorCode.ValidationFailed)]
    [InlineData("forbidden", RecordErrorCode.PermissionDenied)]
    [InlineData("conflict", RecordErrorCode.Conflict)]
    public async Task Report_errors_become_record_errors_with_their_message(string kind, RecordErrorCode expected)
    {
        Exception error = kind switch
        {
            "rule" => new ReportException("Dosya sınırı aşıldı."),
            "forbidden" => new ReportForbiddenException("Dosya sınırı aşıldı."),
            _ => new ReportConflictException("Dosya sınırı aşıldı."),
        };

        var result = await new ReportTableExports(new FakeExports { Error = error }).CreateAsync(Table, ReportTableFormat.Csv, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal((expected, "Dosya sınırı aşıldı."), (result.Error!.Code, result.Error.Message));
    }

    private static async Task<ReportFileFormat?> FormatOf(ReportTableFormat format)
    {
        var exports = new FakeExports();
        await new ReportTableExports(exports).CreateAsync(Table, format, CancellationToken.None);
        return exports.Format;
    }

    private sealed class FakeExports : IReportExports
    {
        public Exception? Error { get; init; }

        public string? Name { get; private set; }

        public string? DatasetKey { get; private set; }

        public ReportResult? Result { get; private set; }

        public ReportFileFormat? Format { get; private set; }

        public Task<ReportExportInfo> CreateFromResultAsync(string name, string datasetKey, ReportResult result, ReportFileFormat format, CancellationToken cancellationToken)
        {
            if (Error is not null)
            {
                throw Error;
            }

            (Name, DatasetKey, Result, Format) = (name, datasetKey, result, format);
            return Task.FromResult(new ReportExportInfo(
                Guid.NewGuid(), null, name, datasetKey, format == ReportFileFormat.Xlsx ? "Satış kartı.xlsx" : "Satış kartı.csv", 10, result.Rows.Count, result.Truncated,
                ReportExportSource.Manual, Now, Now.AddHours(24), 0, false));
        }

        public Task<ReportExportInfo?> CreateAsync(Guid reportId, CancellationToken cancellationToken, ReportFileFormat format = ReportFileFormat.Csv) => throw new NotSupportedException();

        public Task<ReportExportInfo> CreateAsync(ReportSpec spec, string? name, CancellationToken cancellationToken, ReportFileFormat format = ReportFileFormat.Csv) => throw new NotSupportedException();

        public Task<ReportExportInfo?> CreateScheduledAsync(Guid reportId, CancellationToken cancellationToken, ReportFileFormat format = ReportFileFormat.Csv) => throw new NotSupportedException();

        public Task<IReadOnlyList<ReportExportInfo>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReportDownload> DownloadAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
