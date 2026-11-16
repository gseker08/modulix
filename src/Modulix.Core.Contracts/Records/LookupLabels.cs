namespace Modulix.Core.Contracts.Records;

/// <summary>Kimlikten çözülmüş etiket. Erişilemeyen kayıt için <see cref="Label"/> boş, <see cref="Unavailable"/> doludur.</summary>
public sealed record LookupLabel(Guid Id, string? Label, bool Unavailable);

/// <summary>
/// Lookup kimliklerinden etiket çözümü (ADR-0046): modül uç noktası (ör. dışa aktarmada karşı taraf adı) Core'un lookup servisini doğrudan görmez.
/// Kurallar lookup aramasıyla aynıdır: tenant, modül hakkı, izin, veri kapsamı ve hassas alanlar hedef kaydın kurallarıyla uygulanır.
/// </summary>
public interface ILookupLabels
{
    /// <summary>En çok <see cref="RecordLookupLimits.MaxResolveIds"/> kimlik; fazlası doğrulama hatasıdır.</summary>
    Task<RecordResult<IReadOnlyList<LookupLabel>>> ResolveAsync(string lookupKey, IReadOnlyList<Guid> ids, CancellationToken cancellationToken);
}
