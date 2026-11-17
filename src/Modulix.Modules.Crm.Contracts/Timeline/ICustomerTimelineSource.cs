namespace Modulix.Modules.Crm.Contracts.Timeline;

/// <summary>
/// Müşteri zaman çizelgesi sorgusu: <paramref name="CompanyId"/> CRM firma kaydının, <paramref name="PartyId"/> aynı firmanın ortak (Core) taraf
/// kaydının kimliğidir; kaynak kendi kayıtlarını hangisine bağlıyorsa onu kullanır. <paramref name="Before"/> verilirse yalnızca o andan önceki
/// satırlar istenir (sayfalama imleci; sınır hariç). <paramref name="Take"/> kaynağın döndüreceği en çok satır sayısıdır.
/// </summary>
public sealed record CustomerTimelineQuery(Guid CompanyId, Guid? PartyId, DateTimeOffset? Before, int Take);

/// <summary>
/// Zaman çizelgesindeki tek satır. <paramref name="EntityKey"/> kaydın türü (ör. <c>crm.activity</c>), <paramref name="OccurredAt"/> satırın
/// çizelgedeki anıdır. <paramref name="Status"/> ve <paramref name="Type"/> kaynağın teknik kodlarıdır (görünen ad değil); <paramref name="Reference"/>
/// belge numarası gibi kısa bir tanıtıcıdır. Satır yalnızca çağıranın o modülde görebildiği kayıt için üretilir.
/// </summary>
public sealed record CustomerTimelineEntry(
    string EntityKey,
    Guid RecordId,
    DateTimeOffset OccurredAt,
    string Title,
    string? Status = null,
    string? Type = null,
    Guid? OwnerId = null,
    string? Reference = null);

/// <summary>Bir kaynağın dilimi: en yeniden eskiye satırlar ve bu dilimden daha eski satır kalıp kalmadığı.</summary>
public sealed record CustomerTimelineSlice(IReadOnlyList<CustomerTimelineEntry> Entries, bool HasMore);

/// <summary>
/// Müşteri kartındaki birleşik zaman çizelgesinin kaynağı (CRM eksik süreçler E9). Kart CRM'e aittir; kendi kayıtlarını çizelgede göstermek
/// isteyen her modül bu sözleşmeyi uygular ve kaydeder, CRM kaynakları birleştirir. Modüller birbirinin tablosunu okumaz.
/// <para>
/// Kaynak satırları çağıranın kimliğiyle, kendi izin, veri kapsamı ve lisans kurallarıyla üretir. Çağıran bu kaynağı okuyamıyorsa (izin yok ya da
/// modül kapalı) <c>null</c> döner; boş dilim "okunabilir ama kayıt yok" demektir. Satırlar <see cref="CustomerTimelineEntry.OccurredAt"/> alanına
/// göre en yeniden eskiye sıralı ve en çok <see cref="CustomerTimelineQuery.Take"/> adet olmalıdır.
/// </para>
/// </summary>
public interface ICustomerTimelineSource
{
    /// <summary>Kaynağın anahtarı; modül anahtarıyla başlar (ör. <c>crm.activities</c>). Yanıtta hangi kaynakların okunduğunu bildirir.</summary>
    string Key { get; }

    Task<CustomerTimelineSlice?> ReadAsync(CustomerTimelineQuery query, CancellationToken cancellationToken);
}
