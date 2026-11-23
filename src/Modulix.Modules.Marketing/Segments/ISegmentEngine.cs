using Modulix.Core.Contracts.Localization;
using Modulix.Modules.Marketing.Contracts;

namespace Modulix.Modules.Marketing.Segments;

/// <summary>Çağıranın segmentte kullanabildiği alanlar ve kullanamadıkları (kaynak modül kapalı ya da okuma yetkisi yok).</summary>
public sealed record SegmentCatalog(IReadOnlyList<SegmentFieldDefinition> Fields, IReadOnlyList<UnavailableField> Unavailable);

/// <summary>Kullanılamayan alan ve nedeni; olmayan alan çalışıyormuş gibi görünmez, açıklanır.</summary>
public sealed record UnavailableField(string Key, string Label, string Reason);

public sealed record SegmentRow(Guid Id, string? Label);

/// <param name="Count">Hedef sayısı (çağıranın veri kapsamı ve alan yetkisiyle).</param>
/// <param name="Rows">Sayfa satırları; çağıranın alıcı görüntüleme yetkisi yoksa boş.</param>
/// <param name="RowsHidden">Satırlar yetki nedeniyle gizlendi mi?</param>
public sealed record SegmentEvaluation(int Count, IReadOnlyList<SegmentRow> Rows, bool RowsHidden, int DurationMs);

/// <summary>Değerlendirme isteği: koşul (dinamik) ve/veya segmentin dahil/hariç üyeleri.</summary>
public sealed record SegmentEvaluationRequest(
    SegmentTarget Target, SegmentCondition? Condition, Guid? SegmentId, bool IncludeMembers, int Page, int PageSize, bool IncludeRows);

/// <summary>Koşul alan/operatör/değer denetiminden geçmedi.</summary>
public sealed class SegmentConditionException(IReadOnlyList<ConditionError> errors) : Exception(MarketingMessages.SegmentService.ConditionInvalid.Text)
{
    public IReadOnlyList<ConditionError> Errors { get; } = errors;
}

/// <summary>Sonuç, istenen sınırdan büyük (ör. anlık görüntüye sığmaz).</summary>
public sealed class SegmentTooLargeException(int count, int limit) : Exception(MarketingMessages.SegmentService.TooLarge(MarketingMessages.N(count), MarketingMessages.N(limit)).Text)
{
    /// <summary>Çevrilebilir ileti (HTTP sınırında isteğin diline çevrilir).</summary>
    public ServerMessage Reason { get; } = MarketingMessages.SegmentService.TooLarge(MarketingMessages.N(count), MarketingMessages.N(limit));

    public int Count { get; } = count;

    public int Limit { get; } = limit;
}

/// <summary>Hesaplama süre sınırını aştı.</summary>
public sealed class SegmentTimeoutException(TimeSpan limit) : Exception(MarketingMessages.SegmentService.Timeout(limit.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Text)
{
    /// <summary>Çevrilebilir ileti (HTTP sınırında isteğin diline çevrilir).</summary>
    public ServerMessage Reason { get; } = MarketingMessages.SegmentService.Timeout(limit.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// Segment motoru (ADR-0025 K3/K4): koşul ağacını alan kaynaklarının kimlik sorgularından kümeler cebriyle kurar, çağıranın alan
/// yetkisi ve veri kapsamıyla sayar ve önizler. Yazmaz.
/// </summary>
public interface ISegmentEngine
{
    Task<SegmentCatalog> CatalogAsync(SegmentTarget target, CancellationToken cancellationToken);

    /// <summary>Geçersiz koşulda <see cref="SegmentConditionException"/>, süre aşımında <see cref="SegmentTimeoutException"/>.</summary>
    Task<SegmentEvaluation> EvaluateAsync(SegmentEvaluationRequest request, CancellationToken cancellationToken);

    /// <summary>Hedef kimliklerinin tamamı (sıralı). <paramref name="limit"/> aşılırsa <see cref="SegmentTooLargeException"/>; diğer hatalar <see cref="EvaluateAsync"/> gibidir.</summary>
    Task<IReadOnlyList<Guid>> ResolveIdsAsync(SegmentEvaluationRequest request, int limit, CancellationToken cancellationToken);
}
