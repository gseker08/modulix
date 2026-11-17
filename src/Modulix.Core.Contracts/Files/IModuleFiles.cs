using Modulix.Core.Contracts.Records;

namespace Modulix.Core.Contracts.Files;

/// <summary>
/// Modüle ait dosyalar (ADR-0040 K9). Erişim kuralı bağlı kaydın erişiminden farklı olan ekler içindir (ör. destek talebinde iç nota bağlı ek: talebi
/// okuyabilen herkes göremez; ek silinemez). İçerik ortak depoya <c>ModuleOwned</c> sınıfıyla yazılır: genel dosya uçları (<see cref="IFileStore"/>,
/// <c>/api/files</c>) bu sınıfı listelemez, indirmez ve silmez. <b>Yetki, doğrulama ve sınırlar çağıran modülün sorumluluğundadır</b>; bu servis
/// erişim denetimi yapmaz, yalnızca aktif tenant içinde çalışır. Yetim içerik temizliği, tenant dışa aktarma ve tenant silme bu dosyaları da kapsar.
/// </summary>
public interface IModuleFiles
{
    /// <summary>
    /// İçeriği depoya yazar ve üst veriyi <b>çalışan iş birimine ekler</b>; kaydı çağıranın işlemi kesinleştirir (kayıt boru hattının
    /// <c>SaveChanges</c>'i). Böylece dosya, modülün kendi satırıyla aynı işlemde oluşur; işlem geri alınırsa içerik yetim kalır ve temizlik işi siler.
    /// <paramref name="owner"/> dosyanın bağlı olduğu kayıttır (izleme ve kayıt silinince temizlik için); ad ve tür çağıranın doğruladığı değerlerdir.
    /// İçerik boşsa ya da <see cref="IFileLimits.MaxFileBytes"/> sınırını aşarsa <see cref="ArgumentException"/>.
    /// </summary>
    Task<StoredFileInfo> AddAsync(RecordRef owner, string fileName, string contentType, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>
    /// Modüle ait dosyanın içeriğini açar; dosya yoksa, başka tenant'taysa, modüle ait değilse ya da bağlı kayıt türü
    /// <paramref name="ownerEntityKey"/> değilse <c>null</c>. Çağıran, kullanıcının bu dosyayı görebildiğini <b>önceden</b> denetlemiş olmalıdır.
    /// </summary>
    Task<StoredFileContent?> OpenAsync(Guid fileId, string ownerEntityKey, CancellationToken cancellationToken);
}
