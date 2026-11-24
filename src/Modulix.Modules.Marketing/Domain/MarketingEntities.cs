using Modulix.Core.Contracts.Persistence;
using Modulix.Core.Contracts.Tenancy;

namespace Modulix.Modules.Marketing.Domain;

// Pazarlama modülü entity'leri (ADR-0025). Alanlar kayıt depolarından (Records) doğrulanarak yazılır. Firma/kişi ortak kayıttır
// (core.Parties / core.Persons); CRM tablolarına bağ yoktur. Segment hedefi bir kimliktir: firma → taraf, kişi → ortak kişi, lead → lead.

/// <summary>Tüm pazarlama kayıtlarının ortak alanları. TenantId kaydederken aktif tenant'tan atanır.</summary>
public abstract class MarketingEntity : ITenantEntity, ICompanyShared
{
    protected MarketingEntity()
    {
        Id = Guid.CreateVersion7();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public DateTimeOffset CreatedAt { get; internal set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Her değişiklikte artar; eşzamanlı güncellemeleri yakalamak için kullanılır (409).</summary>
    public long Version { get; private set; }

    /// <summary>Firmanın tanımladığı özel alanların değerleri, JSON nesnesi; boşsa null.</summary>
    public string? CustomFields { get; internal set; }

    internal void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
    }
}

/// <summary>Bir kullanıcıya atanabilen kayıt. Sorumlu, aktif tenant'ın etkin üyesi olmalıdır.</summary>
public interface IOwnedEntity
{
    Guid? OwnerId { get; }
}

public static class SegmentTargets
{
    public const string Company = "Company";
    public const string Person = "Person";
    public const string Lead = "Lead";

    public static IReadOnlyList<string> All { get; } = [Company, Person, Lead];
}

public static class SegmentKinds
{
    /// <summary>Üyeler elle seçilir (üye satırları).</summary>
    public const string Static = "Static";

    /// <summary>Üyeler yayımlanan koşul sürümünden hesaplanır; isteğe bağlı dahil/hariç istisnalar eklenebilir.</summary>
    public const string Dynamic = "Dynamic";

    public static IReadOnlyList<string> All { get; } = [Static, Dynamic];
}

public static class SegmentStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";

    /// <summary>Arşivlenen segment geçmiş raporlarda kalır, yeni kampanyada seçilemez.</summary>
    public const string Archived = "Archived";

    public static IReadOnlyList<string> All { get; } = [Draft, Active, Archived];
}

public static class MemberKinds
{
    /// <summary>Koşuldan bağımsız dahil edilir.</summary>
    public const string Include = "Include";

    /// <summary>Koşulu sağlasa da hariç tutulur (istisna); hariç tutma dahil etmenin önüne geçer.</summary>
    public const string Exclude = "Exclude";

    public static IReadOnlyList<string> All { get; } = [Include, Exclude];
}

/// <summary>
/// Segment: belirli iş kriterlerini sağlayan firma, kişi veya lead grubu. Hedef türü ve tür (statik/dinamik) oluşturulduktan sonra değişmez;
/// karışık türler tek segmentte yoktur. Dinamik segmentin üyeleri hesaplanır ve satır olarak saklanmaz (ADR-0025 K6); son hesaplama
/// özeti segmentte durur. Taslak koşul düzenlenir; yayımlanan sürüm değişmez (<see cref="SegmentVersion"/>).
/// </summary>
public sealed class Segment : MarketingEntity, IOwnedEntity
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 1000;
    public const int PurposeMaxLength = 200;
    public const int DraftMaxLength = 20000;
    public const int ErrorMaxLength = 500;

    public string Code { get; internal set; } = string.Empty;

    public string Name { get; internal set; } = string.Empty;

    public string? Description { get; internal set; }

    public string Target { get; internal set; } = SegmentTargets.Company;

    public string Kind { get; internal set; } = SegmentKinds.Dynamic;

    public string? Purpose { get; internal set; }

    public Guid? OwnerId { get; internal set; }

    public string Status { get; internal set; } = SegmentStatuses.Draft;

    /// <summary>Taslak koşul ağacı (JSON); yalnızca dinamik segmentte. Yayımlanınca <see cref="SegmentVersion"/> olarak dondurulur.</summary>
    public string? Draft { get; internal set; }

    /// <summary>Son yayın sürümü numarası; 0 = hiç yayımlanmadı.</summary>
    public int PublishedVersion { get; internal set; }

    public DateTimeOffset? LastComputedAt { get; internal set; }

    public int? LastMemberCount { get; internal set; }

    public int? LastComputeMs { get; internal set; }

    public string? LastError { get; internal set; }

    public Guid? CreatedBy { get; internal set; }

    public Guid? UpdatedBy { get; internal set; }
}

/// <summary>Yayımlanmış koşul sürümü (değişmez). Kampanya hedefi sürüm numarasına bağlanır; sonradan segment değişse geçmiş hedef değişmez.</summary>
public sealed class SegmentVersion : MarketingEntity
{
    public Guid SegmentId { get; internal set; }

    public int VersionNo { get; internal set; }

    /// <summary>Koşul ağacı (JSON).</summary>
    public string Definition { get; internal set; } = string.Empty;

    public DateTimeOffset PublishedAt { get; internal set; }

    public Guid? PublishedBy { get; internal set; }
}

/// <summary>Segmentin dahil/hariç üyesi (statik üye ya da dinamik segmentte istisna). (Segment, hedef) tekildir.</summary>
public sealed class SegmentMember : MarketingEntity
{
    public const int ReasonMaxLength = 200;

    public Guid SegmentId { get; internal set; }

    /// <summary>Hedef kimliği (segmentin hedef türüne göre taraf, ortak kişi ya da lead kimliği).</summary>
    public Guid TargetId { get; internal set; }

    public string Kind { get; internal set; } = MemberKinds.Include;

    public string? Reason { get; internal set; }

    public Guid? AddedBy { get; internal set; }
}

public static class JourneyStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Paused = "Paused";

    /// <summary>Arşivlenen yolculuk yeni kayıt almaz; geçmiş raporlarda kalır.</summary>
    public const string Archived = "Archived";

    public static IReadOnlyList<string> All { get; } = [Draft, Active, Paused, Archived];
}

/// <summary>Bir hedefin aynı yolculuğa tekrar girip giremeyeceği (ADR-0056). Aynı anda en çok bir etkin kayıt vardır.</summary>
public static class JourneyReentryPolicies
{
    public const string Never = "Never";
    public const string AfterExit = "AfterExit";
    public const string AfterCompletion = "AfterCompletion";

    public static IReadOnlyList<string> All { get; } = [Never, AfterExit, AfterCompletion];
}

public static class EnrollmentStatuses
{
    public const string Active = "Active";
    public const string Completed = "Completed";
    public const string Exited = "Exited";

    public static IReadOnlyList<string> All { get; } = [Active, Completed, Exited];
}

/// <summary>
/// Müşteri yolculuğu (ADR-0056). Giriş kaynağı segmenttir (dilim 1); hedef türü segmentinkiyle aynıdır ve oluşturulduktan sonra değişmez.
/// Durum yalnızca iş akışıyla yazılır (genel PUT yok sayar). Adımlar ADR-0027 kural sürümündedir; bu kayıt yolculuğu ve girişini yönetir.
/// </summary>
public sealed class Journey : MarketingEntity, IOwnedEntity
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 1000;
    public const int ExitConditionMaxLength = 20000;
    public const int EntryPermissionsMaxLength = 8000;

    public string Code { get; internal set; } = string.Empty;

    public string Name { get; internal set; } = string.Empty;

    public string? Description { get; internal set; }

    public string Target { get; internal set; } = SegmentTargets.Person;

    /// <summary>
    /// Yolculuğu etkinleştirenin (yayın ya da sürdürme) okuma izinleri (JSON dizi). Giriş işi segmenti bu kümeyle değerlendirir (ADR-0056 dilim 1b-2);
    /// kayıt alanı değildir, istekle yazılmaz ve yanıtta görünmez.
    /// </summary>
    public string? EntryPermissions { get; internal set; }

    /// <summary>Giriş segmenti (yayımlanmış, etkin ve aynı hedef türünde olmalıdır).</summary>
    public Guid SegmentId { get; internal set; }

    public string ReentryPolicy { get; internal set; } = JourneyReentryPolicies.Never;

    /// <summary>Her adımdan önce değerlendirilen çıkış koşulu (JSON koşul ağacı); boşsa yalnızca abonelikten çıkma çıkarır.</summary>
    public string? ExitCondition { get; internal set; }

    public Guid? OwnerId { get; internal set; }

    public string Status { get; internal set; } = JourneyStatuses.Draft;

    public Guid? CreatedBy { get; internal set; }

    public Guid? UpdatedBy { get; internal set; }
}

/// <summary>Yolculuk iletisi sonuçları (<see cref="JourneyMessage.Outcome"/>).</summary>
public static class JourneyMessageOutcomes
{
    /// <summary>Gönderim hakkı alındı, sonuç yazılmadı (çökme durumunda kalır; kör yeniden deneme yoktur).</summary>
    public const string Processing = "Processing";
    public const string Accepted = "Accepted";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";
    public const string Unknown = "Unknown";
}

/// <summary>
/// Yolculuk iletisi günlüğü (ADR-0056 dilim 2a): bir yolculuk kaydının bir adımında gönderilen (ya da izin, sıklık, adres gibi nedenlerle gönderilmeyen) ileti.
/// Kayıt + adım başına tek satır vardır (benzersiz indeks): aynı adım yeniden çağrılsa ileti ikinci kez gitmez. Adres saklanmaz; yalnızca sonuç ve neden kodu tutulur.
/// </summary>
public sealed class JourneyMessage : IJournalEntity, ICompanyShared
{
    public const int StepKeyMaxLength = 80;
    public const int ChannelMaxLength = 20;
    public const int OutcomeMaxLength = 20;
    public const int CodeMaxLength = 60;
    public const int ProviderMessageIdMaxLength = 200;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; private set; }

    public Guid JourneyId { get; set; }

    public Guid EnrollmentId { get; set; }

    public string StepKey { get; set; } = string.Empty;

    public Guid ContentId { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string Outcome { get; set; } = JourneyMessageOutcomes.Processing;

    public string? Code { get; set; }

    public string? ProviderMessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Bir hedefin yolculuktaki kaydı (ADR-0056). Aynı hedef aynı yolculukta aynı anda bir kez etkin olabilir (filtreli benzersiz indeks).</summary>
public sealed class JourneyEnrollment : MarketingEntity
{
    public const int ExitReasonMaxLength = 200;
    public const int ResponseKindMaxLength = 20;

    public Guid JourneyId { get; internal set; }

    public Guid TargetId { get; internal set; }

    public string Status { get; internal set; } = EnrollmentStatuses.Active;

    public DateTimeOffset EnteredAt { get; internal set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ExitedAt { get; internal set; }

    public string? ExitReason { get; internal set; }

    /// <summary>
    /// Kayıt sona erdikten sonra hedefin giriş segmentinde <b>bulunmadığının</b> görüldüğü ilk an (ADR-0056 dilim 2c-2). Yeniden giriş yalnızca bu işaret doluysa olur:
    /// segmentte kalan hedef, kaydı sona erse de sürekli yeniden girmez; "çıkıp yeniden girmek" budur.
    /// </summary>
    public DateTimeOffset? SegmentLeftAt { get; internal set; }

    /// <summary>
    /// Hedefin yolculuk iletisine verdiği son yanıtın türü (<see cref="ResponseKinds"/>; ADR-0056 dilim 3). Kuralın adım koşulu bu alana bakarak dallanır
    /// ("yanıt verdi mi", "ilgilendi mi"); yanıt yoksa boştur.
    /// </summary>
    public string? LastResponseKind { get; internal set; }

    public DateTimeOffset? LastResponseAt { get; internal set; }
}
