namespace Modulix.Modules.Support.Domain;

// Kanal, ek ve memnuniyet (ADR-0024, dilim 4). Ek içeriği ortak dosya deposunda modüle ait dosya olarak durur (ADR-0040 K9); üst veri ve erişim kuralı buradadır.

/// <summary>
/// Mesaja bağlı ek. Görünürlük mesajdan gelir: iç nota bağlı ek iç not iznine bağlıdır. İçerik değiştirilmez ve silinmez; dosya türü uzantı ile
/// dosya imzasının (magic number) uyuşmasıyla doğrulanır. İçerik taraması yapılmaz (<see cref="ScanStatus"/> bunu dürüstçe söyler).
/// </summary>
public sealed class TicketAttachment : SupportEntity
{
    public const int FileNameMaxLength = 200;
    public const int ContentTypeMaxLength = 120;
    public const int HashLength = 64;

    public Guid TicketId { get; internal set; }

    public Guid MessageId { get; internal set; }

    public string FileName { get; internal set; } = string.Empty;

    /// <summary>Uzantıdan türetilen güvenli tür; istemcinin bildirdiği tür güvenilmez ve saklanmaz.</summary>
    public string ContentType { get; internal set; } = string.Empty;

    public int SizeBytes { get; internal set; }

    /// <summary>İçeriğin SHA-256 özeti (onaltılık, büyük harf).</summary>
    public string Sha256 { get; internal set; } = string.Empty;

    /// <summary><see cref="Lifecycle.AttachmentScan"/>. Bugün her ek <c>NotScanned</c>'dir: kötü amaçlı yazılım taraması yoktur.</summary>
    public string ScanStatus { get; internal set; } = Lifecycle.AttachmentScan.NotScanned;

    /// <summary>
    /// İçeriğin ortak dosya deposundaki kimliği (ADR-0040 K9, modüle ait dosya). Doluysa içerik depodadır; boşsa henüz taşınmamış eski ektir ve
    /// içerik <see cref="Content"/> sütunundadır (zamanlanmış iş taşır).
    /// </summary>
    public Guid? FileId { get; internal set; }

    /// <summary>Eski saklama yeri: yalnızca depoya taşınmamış eklerde doludur. Yeni ekte boştur; sütun sonraki sürümde düşürülür.</summary>
    public byte[] Content { get; internal set; } = [];

    public Guid? CreatedBy { get; internal set; }
}

/// <summary>
/// Talep için gönderilen memnuniyet anketi. Belirteç yalnızca oluşturulurken bir kez üretilir; veritabanında yalnızca SHA-256 özeti durur.
/// Anket süreli ve tek kullanımlıdır: yanıtlanınca ikinci yanıt reddedilir. Yeniden açılıp tekrar çözülen talebe yeni anket açılır.
/// </summary>
public sealed class SatisfactionSurvey : SupportEntity
{
    public const int CommentMaxLength = 1000;
    public const int EmailMaxLength = 254;

    public Guid TicketId { get; internal set; }

    /// <summary>Anketin ait olduğu çözüm döngüsü (talebin yeniden açılma sayısı + 1).</summary>
    public int Cycle { get; internal set; }

    public string TokenHash { get; internal set; } = string.Empty;

    public string? RecipientEmail { get; internal set; }

    public DateTimeOffset ExpiresAt { get; internal set; }

    /// <summary>Anket e-postasının kuyruğa alındığı an; müşteriye ulaştı demek değildir.</summary>
    public DateTimeOffset? QueuedAt { get; internal set; }

    /// <summary>Hatırlatma e-postasındaki ikinci bağlantının belirteç özeti; hatırlatma gitmediyse boş. İki bağlantı da aynı anketi açar (yanıt yine tek kullanımlıdır).</summary>
    public string? ReminderTokenHash { get; internal set; }

    /// <summary>Hatırlatma e-postasının kuyruğa alındığı an (en çok bir hatırlatma).</summary>
    public DateTimeOffset? ReminderQueuedAt { get; internal set; }

    public DateTimeOffset? AnsweredAt { get; internal set; }

    /// <summary>1 (çok memnuniyetsiz) - 5 (çok memnun); yanıtlanmadıysa boş.</summary>
    public int? Rating { get; internal set; }

    public string? Comment { get; internal set; }
}
