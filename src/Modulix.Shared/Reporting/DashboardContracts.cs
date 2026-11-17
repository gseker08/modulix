namespace Modulix.Shared.Reporting;

// Kullanıcı panoları (CRM epik E2, dilim 1): /api/dashboards. Pano yalnızca sahibine görünür; bileşen verisi bakan kişinin yetkisiyle gelir.

/// <summary><see cref="Kind"/>: number ya da bar.</summary>
public sealed record DashboardWidgetRequest(Guid ReportId, string Kind, string? Title = null);

/// <summary>Adı ve bileşen listesini bütün olarak yazar (sıra listedeki sıradır). <see cref="ExpectedVersion"/> yalnızca güncellemede.</summary>
public sealed record SaveDashboardRequest(string Name, IReadOnlyList<DashboardWidgetRequest>? Widgets = null, long? ExpectedVersion = null);

/// <summary><see cref="ReportName"/> boşsa bakan kişi raporu artık göremiyor; bileşen veri göstermez.</summary>
public sealed record DashboardWidgetDto(Guid Id, Guid ReportId, string? ReportName, string Kind, string? Title, int Position);

public sealed record DashboardSummaryDto(Guid Id, string Name, int WidgetCount, DateTimeOffset UpdatedAt, long Version);

public sealed record DashboardDetailDto(DashboardSummaryDto Summary, IReadOnlyList<DashboardWidgetDto> Widgets);
