using Modulix.Shared.Platform;

namespace Modulix.Web.Services;

// Bu dosyadaki tablolarda Türkçe ad kaynak metindir (ADR-0034, Platform P1); etkin dilin sözlüğünde `{grup}.{anahtar küçük harf}` yoksa o gösterilir.
// Paylaşılan sözleşmelerdeki (Shared) etiketler (TenantStateText, LicenseSourceText, EventStateText, PlatformAuditText, PlatformRoleNames)
// sunucuyla ortak sözleşmede durduğu için Türkçe kalır; kaynak metin onlardan okunur. Tablolar özellik olarak tanımlıdır: metinler her okunuşta
// çözülür (sözlük sonradan yüklenir). Yeni değer eklenince üç sözlüğe de anahtar eklenir (`PlatformLabelsTests` kapsamı denetler).
// Platform P2: sektör ağacı düğüm türü ve hazırlık durumu ile rejim profili sürüm durumu ve ihlal düzeyi ekrana gömülü tablolardan buraya
// taşındı (`PlatformCatalogLabelsTests` denetler).

/// <summary>Platform yönetimi ekranlarının (firmalar, modül hakları, ekip, olaylar, denetim, ödemeler, sektörler, rejim profilleri) etiketleri.</summary>
public static class PlatformLabels
{
    public const string TenantStateGroup = "platform.tenant-state";
    public const string LicenseSourceGroup = "platform.license-source";
    public const string EventStateGroup = "platform.event-state";
    public const string AuditActionGroup = "platform.audit-action";
    public const string RoleGroup = "platform.role";
    public const string BillingStatusGroup = "platform.billing-status";
    public const string NodeKindGroup = "platform.node-kind";
    public const string NodeReadinessGroup = "platform.node-readiness";
    public const string RegimeStateGroup = "platform.regime-state";
    public const string EnforcementGroup = "platform.enforcement";

    /// <summary>Sektör ağacının kök düğüm türü; alt kayıt formunda seçilemez.</summary>
    public const string SectorKind = "Sector";

    /// <summary>Operasyon modülü hazır olan faaliyetin hazırlık değeri; diğer her değer "Yalnızca katalog" sayılır.</summary>
    public const string Available = "Available";

    /// <summary>Rejim profili sürümünün yerini yenisine bırakmış durumu; tanınmayan durum da böyle gösterilir.</summary>
    public const string Superseded = "Superseded";

    /// <summary>Ödeme kaydının ekrandaki durumu; vadesi geçen bekleyen kayıt <c>Overdue</c> olarak gösterilir.</summary>
    public const string Overdue = "Overdue";

    internal static IReadOnlyList<(string Key, string Label)> TenantStateSource =>
        [.. Enum.GetValues<TenantState>().Select(s => (s.ToString(), TenantStateText.Of(s)))];

    internal static IReadOnlyList<(string Key, string Label)> LicenseSourceSource =>
        [.. Enum.GetValues<LicenseSource>().Select(s => (s.ToString(), LicenseSourceText.Of(s)))];

    internal static IReadOnlyList<(string Key, string Label)> EventStateSource =>
        [.. Enum.GetValues<EventDeliveryStateDto>().Select(s => (s.ToString(), EventStateText.Of(s)))];

    internal static IReadOnlyList<(string Key, string Label)> AuditActionSource => PlatformAuditText.All;

    internal static IReadOnlyList<(string Key, string Label)> RoleSource =>
        [.. new[] { PlatformRoleNames.Support, PlatformRoleNames.Operations, PlatformRoleNames.Finance, PlatformRoleNames.Owner }
            .Select(r => (r, PlatformRoleNames.TextOf(r)))];

    internal static IReadOnlyList<(string Key, string Label)> BillingStatusSource =>
    [
        (nameof(BillingStatusDto.Pending), "Bekliyor"),
        (Overdue, "Vadesi geçti"),
        (nameof(BillingStatusDto.Paid), "Ödendi"),
        (nameof(BillingStatusDto.Cancelled), "İptal"),
    ];

    internal static IReadOnlyList<(string Key, string Label)> NodeKindSource =>
    [
        (SectorKind, "Sektör"),
        ("Group", "Faaliyet grubu"),
        ("Activity", "Alt faaliyet"),
    ];

    internal static IReadOnlyList<(string Key, string Label)> NodeReadinessSource =>
    [
        ("CatalogOnly", "Yalnızca katalog"),
        (Available, "Hazır"),
    ];

    internal static IReadOnlyList<(string Key, string Label)> RegimeStateSource =>
    [
        ("Draft", "Taslak"),
        ("Published", "Yayında"),
        (Superseded, "Yerini bıraktı"),
    ];

    internal static IReadOnlyList<(string Key, string Label)> EnforcementSource =>
    [
        ("Warn", "Uyarı (kaydı engellemez)"),
        ("Block", "Engel (kaydı reddeder)"),
    ];

    public static IReadOnlyList<(string Key, string Label)> TenantStates => AdminLabels.Translate(TenantStateGroup, TenantStateSource);

    public static IReadOnlyList<(string Key, string Label)> LicenseSources => AdminLabels.Translate(LicenseSourceGroup, LicenseSourceSource);

    public static IReadOnlyList<(string Key, string Label)> EventStates => AdminLabels.Translate(EventStateGroup, EventStateSource);

    /// <summary>Platform denetim kaydı işlemleri, süzgeçteki sırayla.</summary>
    public static IReadOnlyList<(string Key, string Label)> AuditActions => AdminLabels.Translate(AuditActionGroup, AuditActionSource);

    public static IReadOnlyList<(string Key, string Label)> Roles => AdminLabels.Translate(RoleGroup, RoleSource);

    public static IReadOnlyList<(string Key, string Label)> BillingStatuses => AdminLabels.Translate(BillingStatusGroup, BillingStatusSource);

    /// <summary>Sektör ağacı düğüm türleri (sektör, faaliyet grubu, alt faaliyet).</summary>
    public static IReadOnlyList<(string Key, string Label)> NodeKinds => AdminLabels.Translate(NodeKindGroup, NodeKindSource);

    /// <summary>Faaliyetin operasyon modülü hazırlığı.</summary>
    public static IReadOnlyList<(string Key, string Label)> NodeReadinesses => AdminLabels.Translate(NodeReadinessGroup, NodeReadinessSource);

    public static IReadOnlyList<(string Key, string Label)> RegimeStates => AdminLabels.Translate(RegimeStateGroup, RegimeStateSource);

    /// <summary>Tescil numarası kuralının ihlal düzeyi seçenekleri.</summary>
    public static IReadOnlyList<(string Key, string Label)> Enforcements => AdminLabels.Translate(EnforcementGroup, EnforcementSource);

    /// <summary>Bilinmeyen tür olduğu gibi gösterilir.</summary>
    public static string NodeKindOf(string kind) => AdminLabels.Lookup(NodeKindGroup, NodeKindSource, kind);

    /// <summary><c>Available</c> dışındaki her değer "Yalnızca katalog" gösterilir.</summary>
    public static string NodeReadinessOf(string readiness) =>
        AdminLabels.Lookup(NodeReadinessGroup, NodeReadinessSource, readiness == Available ? Available : "CatalogOnly");

    /// <summary>Taslak ve yayın dışındaki her durum "Yerini bıraktı" gösterilir.</summary>
    public static string RegimeStateOf(string state) =>
        AdminLabels.Lookup(RegimeStateGroup, RegimeStateSource, state is "Draft" or "Published" ? state : Superseded);

    public static string TenantStateOf(TenantState state) => AdminLabels.Of(TenantStateGroup, state.ToString(), TenantStateText.Of(state));

    public static string LicenseSourceOf(LicenseSource source) => AdminLabels.Of(LicenseSourceGroup, source.ToString(), LicenseSourceText.Of(source));

    public static string EventStateOf(EventDeliveryStateDto state) => AdminLabels.Of(EventStateGroup, state.ToString(), EventStateText.Of(state));

    /// <summary>Bilinmeyen işlem anahtarı olduğu gibi gösterilir.</summary>
    public static string AuditActionOf(string action) => AdminLabels.Lookup(AuditActionGroup, AuditActionSource, action);

    /// <summary>Bilinmeyen rol adı olduğu gibi gösterilir.</summary>
    public static string RoleOf(string role) => AdminLabels.Lookup(RoleGroup, RoleSource, role);

    public static string BillingStatusOf(BillingRecordDto item) =>
        AdminLabels.Lookup(BillingStatusGroup, BillingStatusSource, item.Status == BillingStatusDto.Pending && item.IsOverdue ? Overdue : item.Status.ToString());
}
