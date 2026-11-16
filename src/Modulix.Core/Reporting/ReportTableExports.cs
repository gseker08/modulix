using System.Text.Json;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Reporting;

namespace Modulix.Core.Reporting;

/// <summary><see cref="IReportTableExports"/> uygulaması: tabloyu tek sayfalık <see cref="ReportResult"/>'a çevirip <see cref="IReportExports"/> ile dosyaya yazar.</summary>
public sealed class ReportTableExports(IReportExports exports) : IReportTableExports
{
    public async Task<RecordResult<ReportTableExport>> CreateAsync(ReportTable table, ReportTableFormat format, CancellationToken cancellationToken)
    {
        var columns = table.Columns.Select(c => new ReportResultColumn(c.Key, c.Label, c.Kind, ReportColumnRole.Value)).ToList();
        var rows = table.Rows.Select(r => new ReportResultRow(r, [])).ToList();
        var result = new ReportResult(
            ReportRunStatus.Ok, IsSummary: false, columns, rows, Totals: [], RowsRead: rows.Count, RowsMatched: rows.Count, table.Truncated, RowsLimited: false,
            Page: 1, PageSize: rows.Count, table.GeneratedAt, DatasetVersion: 0, Issues: [], DatasetName: table.Title);
        try
        {
            var e = await exports.CreateFromResultAsync(
                table.Name, table.DatasetKey, result, format == ReportTableFormat.Xlsx ? ReportFileFormat.Xlsx : ReportFileFormat.Csv, cancellationToken);
            return RecordResult<ReportTableExport>.Ok(new ReportTableExport(
                e.Id, e.ReportId, e.ReportName, e.DatasetKey, e.FileName, e.SizeBytes, e.RowCount, e.Truncated,
                JsonNamingPolicy.CamelCase.ConvertName(e.Source.ToString()), e.CreatedAt, e.ExpiresAt, e.DownloadCount, e.Expired));
        }
        catch (ReportException ex)
        {
            return RecordResult<ReportTableExport>.Fail(RecordErrorCode.ValidationFailed, ex.Message);
        }
        catch (ReportForbiddenException ex)
        {
            return RecordResult<ReportTableExport>.Fail(RecordErrorCode.PermissionDenied, ex.Message);
        }
        catch (ReportConflictException ex)
        {
            return RecordResult<ReportTableExport>.Fail(RecordErrorCode.Conflict, ex.Message);
        }
    }
}
