namespace Modulix.Shared.Reporting;

// Kullanıcı panoları (CRM epik E2): /api/dashboards. Pano sahibine, paylaşıldığı kullanıcılara ya da firmaya görünür; yalnızca sahibi
// değiştirir. Bileşen verisi bakan kişinin yetkisiyle gelir.

/// <summary><see cref="Kind"/>: number ya da bar.</summary>
public sealed record DashboardWidgetRequest(Guid ReportId, string Kind, string? Title = null);

/// <summary>Adı ve bileşen listesini bütün olarak yazar (sıra listedeki sıradır). <see cref="ExpectedVersion"/> yalnızca güncellemede.</summary>
public sealed record SaveDashboardRequest(string Name, IReadOnlyList<DashboardWidgetRequest>? Widgets = null, long? ExpectedVersion = null);

/// <summary><see cref="ReportName"/> boşsa bakan kişi raporu artık göremiyor; bileşen veri göstermez.</summary>
public sealed record DashboardWidgetDto(Guid Id, Guid ReportId, string? ReportName, string Kind, string? Title, int Position);

/// <summary>
/// Görünürlük: private, users, tenant. <see cref="IsDefault"/> bakan kişiye göredir; <see cref="SharedWithCount"/> yalnızca sahibine dolu gelir.
/// Yeni alanlar sonda ve varsayılanlıdır (önceki sürümün istemcisi bozulmaz).
/// </summary>
public sealed record DashboardSummaryDto(
    Guid Id,
    string Name,
    int WidgetCount,
    DateTimeOffset UpdatedAt,
    long Version,
    string Visibility = "private",
    Guid OwnerUserId = default,
    string? OwnerName = null,
    bool IsOwner = true,
    bool IsDefault = false,
    int SharedWithCount = 0);

/// <summary><see cref="SharedWith"/>: paylaşılan kullanıcılar; yalnızca sahibine dolu gelir.</summary>
public sealed record DashboardDetailDto(
    DashboardSummaryDto Summary, IReadOnlyList<DashboardWidgetDto> Widgets, IReadOnlyList<Guid>? SharedWith = null, IReadOnlyList<ShareGroupDto>? SharedWithGroups = null);

/// <summary><see cref="Visibility"/>: private, users ya da tenant; <see cref="Users"/> yalnızca users için.</summary>
public sealed record ShareDashboardRequest(string Visibility, IReadOnlyList<Guid>? Users = null, long? ExpectedVersion = null, IReadOnlyList<ShareGroupDto>? Groups = null);

public sealed record DefaultDashboardRequest(bool IsDefault);

public sealed record DefaultDashboardResponse(bool IsDefault);
