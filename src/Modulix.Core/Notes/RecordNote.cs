using Modulix.Core.Contracts.Tenancy;

namespace Modulix.Core.Notes;

/// <summary>
/// Kayda bağlı not (ADR-0057). Bağlı kayıt <see cref="ParentEntityKey"/> + <see cref="ParentId"/> ile gösterilir; ikisi de oluşturulduktan sonra değişmez.
/// Çekirdek yalnızca saklar: nota erişimi bağlı kaydın erişimine bağlıdır (kayıt deposu uygular). Dosyaları Core dosya deposundadır ve nota bağlanır.
/// Tenant verisidir; Row-Level Security ile korunur. Şirketler arasında ortaktır: kapsamı bağlı kaydın kapsamıdır.
/// </summary>
public sealed class RecordNote : ITenantEntity, ICompanyShared
{
    public const int ParentEntityKeyMaxLength = 100;
    public const int BodyMaxLength = 4000;

    public RecordNote()
    {
        Id = Guid.CreateVersion7();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>Bağlı kaydın türü (ör. <c>crm.company</c>); oluşturulduktan sonra değişmez.</summary>
    public string ParentEntityKey { get; set; } = string.Empty;

    /// <summary>Bağlı kaydın kimliği; oluşturulduktan sonra değişmez.</summary>
    public Guid ParentId { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>Notu yazan kullanıcı; notu yalnızca o değiştirir ve siler. Üyelik yabancı anahtarı yoktur (üyelik kalksa not kalır).</summary>
    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Her değişiklikte artar; eşzamanlı güncellemeleri yakalamak için kullanılır (409).</summary>
    public long Version { get; private set; }

    public void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
    }
}
