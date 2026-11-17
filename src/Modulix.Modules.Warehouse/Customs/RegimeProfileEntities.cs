using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Warehouse.Contracts.Customs;

namespace Modulix.Modules.Warehouse.Customs;

/// <summary>
/// Rejim profili (ADR-0032 K16): ülke/rejim/antrepo tipine göre değişen kuralların sürümlü paketi. Platform geneli kayıttır (RLS dışı, <c>TenantId</c>
/// yok); yalnızca platform yönetir. Profilin içeriği <see cref="RegimeProfileVersion"/>'dadır. Profil silinmez, pasife alınır
/// (tenant seçimleri ona başvurur).
/// </summary>
public sealed class RegimeProfile : IPlatformReferenceEntity
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 100;

    private RegimeProfile()
    {
        // EF Core için
    }

    public RegimeProfile(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > CodeMaxLength || !code.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            throw new ArgumentException($"Geçersiz profil kodu: '{code}' (küçük harf, rakam ve '-'; en çok {CodeMaxLength} karakter).", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            throw new ArgumentException($"Profil adı boş olamaz ve en çok {NameMaxLength} karakter olabilir.", nameof(name));
        }

        Id = Guid.CreateVersion7();
        Code = code;
        Name = name.Trim();
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>
/// Profilin bir sürümü. Profil başına en çok bir taslak ve bir yayın vardır. Yayınlanan içerik değişmez; kayıtlar işlendikleri sürüme bağlı
/// kalır (K16). <see cref="IsVerified"/> kuralın uzman tarafından doğrulandığını söyler; doğrulanmamış yayın arayüzde "Doğrulanmadı" etiketi taşır.
/// </summary>
public sealed class RegimeProfileVersion : IPlatformReferenceEntity
{
    public const int NoteMaxLength = 500;

    private RegimeProfileVersion()
    {
        // EF Core için
    }

    public RegimeProfileVersion(Guid profileId, int number, string contentJson, Guid createdByUserId)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        Id = Guid.CreateVersion7();
        ProfileId = profileId;
        Number = number;
        ContentJson = contentJson;
        State = RegimeProfileVersionState.Draft;
        CreatedByUserId = createdByUserId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid ProfileId { get; private set; }

    /// <summary>Profil içinde artan sürüm numarası (1'den).</summary>
    public int Number { get; private set; }

    public RegimeProfileVersionState State { get; private set; }

    /// <summary>İçerik (<see cref="RegimeProfileContent"/>) JSON olarak.</summary>
    public string ContentJson { get; private set; } = string.Empty;

    public bool IsVerified { get; private set; }

    public string? VerificationNote { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public Guid? PublishedByUserId { get; private set; }

    /// <summary>Taslağın içeriğini değiştirir (yayınlanmış sürüm değiştirilemez).</summary>
    public void ReplaceContent(string contentJson)
    {
        if (State != RegimeProfileVersionState.Draft)
        {
            throw new InvalidOperationException("Yalnızca taslak sürümün içeriği değiştirilebilir.");
        }

        ContentJson = contentJson;
    }

    public void Publish(bool verified, string? note, Guid publishedByUserId)
    {
        if (State != RegimeProfileVersionState.Draft)
        {
            throw new InvalidOperationException("Yalnızca taslak sürüm yayınlanabilir.");
        }

        State = RegimeProfileVersionState.Published;
        IsVerified = verified;
        VerificationNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        PublishedAt = DateTimeOffset.UtcNow;
        PublishedByUserId = publishedByUserId;
    }

    public void Supersede()
    {
        if (State != RegimeProfileVersionState.Published)
        {
            throw new InvalidOperationException("Yalnızca yayındaki sürümün yerine yenisi geçebilir.");
        }

        State = RegimeProfileVersionState.Superseded;
    }
}
