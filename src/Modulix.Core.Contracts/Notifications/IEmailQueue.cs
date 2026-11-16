namespace Modulix.Core.Contracts.Notifications;

/// <summary>
/// Gönderilecek e-posta. <see cref="HtmlBody"/> zorunludur; <see cref="TextBody"/> verilmezse
/// yalnızca HTML gönderilir. İçerik çağıranın sorumluluğundadır: kullanıcı girdisi HTML'e
/// eklenmeden önce kodlanmalıdır (ör. <c>System.Net.WebUtility.HtmlEncode</c>).
/// </summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody)
{
    public string? ToName { get; init; }

    public string? TextBody { get; init; }

    /// <summary>
    /// Gönderimi izlemek için çağıranın verdiği anahtar (ör. "crm.quote-link:{id}", en çok 100 karakter). Kuyruk satırında saklanır; çağıran bununla
    /// gönderim durumunu sorgular. Alıcıya görünmez, içerik değildir.
    /// </summary>
    public string? Reference { get; init; }

    /// <summary>
    /// İçeriğin yazıldığı dil (ADR-0034 §6; ör. "en"). Kuyruk satırında saklanır; gönderim anında içerik yeniden çevrilmez.
    /// Boşsa dil bilinmiyor demektir (eski ya da dile bağlı olmayan e-posta).
    /// </summary>
    public string? Language { get; init; }
}

/// <summary>
/// E-posta kuyruğu. E-posta hemen gönderilmez: kuyruğa yazılır ve arka plandaki işleyici gönderir;
/// SMTP sunucusuna ulaşılamazsa artan aralıklarla tekrar dener. Aktif tenant varsa e-posta o tenant'a
/// bağlanır.
/// </summary>
public interface IEmailQueue
{
    /// <summary>
    /// E-postayı kuyruğa ekler. Kayıt, çağıranın bir sonraki kaydetmesiyle (kayıt boru hattı veya
    /// <c>SaveChangesAsync</c>) aynı işlemde yazılır; işlem geri alınırsa e-posta da gönderilmez.
    /// Geçersiz adres, boş konu veya sınırı aşan içerik için <see cref="ArgumentException"/> fırlatır.
    /// </summary>
    void Enqueue(EmailMessage message);
}

public enum EmailDeliveryStatus
{
    /// <summary>Gönderilmeyi bekliyor (ilk deneme ya da tekrar deneme zamanı gelmedi).</summary>
    Pending = 1,

    /// <summary>Sağlayıcı (SMTP sunucusu) kabul etti; alıcıya ulaştığı anlamına gelmez.</summary>
    Sent = 2,

    /// <summary>Kalıcı hata ya da deneme hakkı bitti.</summary>
    Failed = 3,
}

/// <summary>Kuyruktaki bir e-postanın gönderim durumu.</summary>
public sealed record EmailDeliveryState(EmailDeliveryStatus Status, int Attempts, string? LastError, DateTimeOffset? SentAt);

/// <summary>
/// Gönderim durumu sorgusu (ADR-0046): modül, kuyruğa yazdığı e-postaların durumunu <see cref="EmailMessage.Reference"/> anahtarıyla sorar; kuyruk
/// tablosunu doğrudan görmez. Yalnızca aktif tenant'ın e-postaları döner; aktif tenant yoksa ya da anahtarın e-postası yoksa sonuçta yer almaz.
/// </summary>
public interface IEmailDeliveries
{
    Task<IReadOnlyDictionary<string, EmailDeliveryState>> GetAsync(IReadOnlyCollection<string> references, CancellationToken cancellationToken);
}

/// <summary>Bildirimin alıcısı: aktif tenant'ın etkin üyesi.</summary>
public sealed record NotificationRecipient(Guid UserId, string DisplayName, string Email);

/// <summary>
/// İnsana giden içerik (ADR-0007): e-posta veya bildirim metnine kaydın bilgisi alıcının gönderim anındaki yetkisine göre
/// konur; olay aboneliğinin izni alıcının izni değildir. Aktif tenant'ta çalışır.
/// </summary>
public interface INotificationRecipients
{
    /// <summary>Aktif tenant'ın etkin üyesiyse adı ve e-postası; değilse (üyeliği kapalı, başka tenant'ın kişisi) null.</summary>
    Task<NotificationRecipient?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Kaydı alıcının o anki yetkisiyle okur: izin, kayıt kapsamı ve hassas alanlar kişinin kendi isteğindeki gibi
    /// uygulanır. Alıcı kaydı göremiyorsa null.
    /// </summary>
    Task<Records.RecordData?> ReadRecordAsync(NotificationRecipient recipient, Records.RecordRef record, CancellationToken cancellationToken);

    /// <summary>Uygulamanın genel adresiyle (App:PublicUrl) arayüz bağlantısı (ör. "crm/firmalar/…"); adres ayarlı değilse null.</summary>
    string? Link(string path);
}
