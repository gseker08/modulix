namespace Modulix.Modules.Warehouse.Contracts.Customs;

/// <summary>Profil sürümünün durumu: taslak → yayında → yerini yenisine bıraktı (geçmiş olarak kalır).</summary>
public enum RegimeProfileVersionState
{
    Draft = 0,
    Published = 1,
    Superseded = 2,
}

public sealed record RegimeProfileSummary(Guid Id, string Code, string Name, bool IsActive, int? PublishedNumber, bool PublishedVerified, bool HasDraft);

public sealed record RegimeProfileVersionView(
    Guid Id, int Number, RegimeProfileVersionState State, bool IsVerified, string? VerificationNote, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt,
    RegimeProfileContent Content);

public sealed record RegimeProfileDetail(RegimeProfileSummary Summary, IReadOnlyList<RegimeProfileVersionView> Versions);

/// <summary>Profil bulunamadı (404).</summary>
public sealed class RegimeProfileNotFoundException(string message) : Exception(message);

/// <summary>Kod çakışması, taslak yokken yayın gibi veri durumundan doğan hata (409).</summary>
public sealed class RegimeProfileConflictException(string message) : Exception(message);

/// <summary>
/// Platform yöneticisinin rejim profili yönetimi. Akış: profil oluştur → taslak içeriği kaydet (doğrulanır) → yayınla (taslak yayına çıkar,
/// önceki yayın "yerini bıraktı" olur). Her yazma platform denetim kaydına aynı işlemde yazılır.
/// </summary>
public interface IRegimeProfileAdmin
{
    Task<IReadOnlyList<RegimeProfileSummary>> ListAsync(CancellationToken cancellationToken);

    Task<RegimeProfileDetail> GetAsync(Guid profileId, CancellationToken cancellationToken);

    Task<RegimeProfileSummary> CreateAsync(Guid actingUserId, string code, string name, CancellationToken cancellationToken);

    /// <summary>Taslağı oluşturur ya da içeriğini değiştirir. İçerik geçersizse <see cref="ArgumentException"/> (hata iletileri birleşik) fırlatır.</summary>
    Task<RegimeProfileVersionView> SaveDraftAsync(Guid actingUserId, Guid profileId, RegimeProfileContent content, CancellationToken cancellationToken);

    Task DiscardDraftAsync(Guid actingUserId, Guid profileId, CancellationToken cancellationToken);

    /// <param name="verified">Kuralın uzman tarafından doğrulandığı. Doğrulanmamış yayın "Doğrulanmadı" etiketiyle yayınlanır.</param>
    Task<RegimeProfileVersionView> PublishAsync(Guid actingUserId, Guid profileId, bool verified, string? note, CancellationToken cancellationToken);

    Task<RegimeProfileSummary> SetActiveAsync(Guid actingUserId, Guid profileId, bool active, CancellationToken cancellationToken);
}
