namespace Modulix.Modules.Crm.Contracts.Timeline;

/// <summary>
/// Müşteri zaman çizelgesi sorgusu: <paramref name="CompanyId"/> CRM firma kaydının, <paramref name="PartyId"/> aynı firmanın ortak (Core) taraf
/// kaydının kimliğidir; kaynak kendi kayıtlarını hangisine bağlıyorsa onu kullanır. <paramref name="Before"/> verilirse yalnızca o andan önceki
/// satırlar istenir (sayfalama imleci; sınır hariç). <paramref name="Take"/> kaynağın döndüreceği en çok satır sayısıdır.
/// <para>
/// Kişi çizelgesi (ADR-0059): <paramref name="ContactId"/> verilirse sorgu o CRM kişisinin kayıtlarınadır (CRM kaynakları kullanır); firma
/// bağlamı <paramref name="CompanyId"/> kişinin ana firması (firmasızsa <see cref="Guid.Empty"/>) ve <paramref name="PartyId"/> o firmanın ortak
/// taraf kaydıdır. <paramref name="PersonId"/> kişinin ortak (Core) kişi kaydının kimliğidir (CRM dışındaki kaynaklar için). Kişi sorgusuna
/// yalnızca <see cref="ICustomerTimelineSource.SupportsPerson"/> bildiren kaynaklar çağrılır.
/// </para>
/// </summary>
public sealed record CustomerTimelineQuery(
    Guid CompanyId, Guid? PartyId, DateTimeOffset? Before, int Take, Guid? ContactId = null, Guid? PersonId = null)
{
    /// <summary>Sorgu bir firmaya değil bir kişiye aittir.</summary>
    public bool IsPerson => ContactId is not null || PersonId is not null;

    /// <summary>
    /// Kayıtları ortak kayda (firmanın ortak tarafı ya da kişinin ortak kişisi) bağlayan kaynakların kullandığı süzgeç: firma sorgusunda
    /// <c>partyId</c>, kişi sorgusunda <c>personId</c>. Bağ yoksa (ortak kaydı olmayan firma ya da kişi) <c>null</c>: okunabilir, boş kaynak.
    /// </summary>
    public (string Field, Guid Id)? SharedRecordScope => IsPerson
        ? PersonId is { } person ? ("personId", person) : null
        : PartyId is { } party ? ("partyId", party) : null;
}

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

    /// <summary>
    /// Kaynak kişi çizelgesini (<see cref="CustomerTimelineQuery.IsPerson"/>) destekler mi (ADR-0059). Varsayılan hayırdır: kaynak kendi kayıtlarını bir
    /// kişiye bağlayabildiğinde bildirir, böylece kişi çizelgesine kendiliğinden girer; bağlayamayan kaynak çağrılmaz ve yanıttaki kaynak listesinde yoktur.
    /// </summary>
    bool SupportsPerson => false;

    Task<CustomerTimelineSlice?> ReadAsync(CustomerTimelineQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Özet şeridi için (ADR-0053, dilim 3): müşterinin bu kaynaktaki açık iş sayısı (ör. kapanmamış destek talebi). Aynı izin, veri kapsamı ve lisans
    /// kuralıyla sayılır. <c>null</c>: kaynak "açık iş" kavramı bildirmiyor ya da çağıran okuyamıyor; sıfır "okunabilir, açık iş yok" demektir.
    /// <paramref name="query"/> içindeki <c>Before</c> ve <c>Take</c> yok sayılır. Varsayılan uygulama bildirmez; kaynaklar isteğe bağlı olarak ezer.
    /// </summary>
    Task<int?> CountOpenAsync(CustomerTimelineQuery query, CancellationToken cancellationToken) => Task.FromResult<int?>(null);
}
