# ADR-0058: Veritabanı okuma anlık görüntüsü (READ_COMMITTED_SNAPSHOT)

- **Durum:** Kabul edildi, 23 Kasım 2026. Yerel/CI kurulumu (`init.sql`) ve test bu belgeyle gelir; canlı veritabanında açmak işletim adımıdır (`docs/tenant-isletim.md`, "Okuma anlık görüntüsü").
- **Karar:** `Modulix` veritabanında `READ_COMMITTED_SNAPSHOT` açık olur. Okuma işlemleri satır kilidi beklemez, son onaylanmış sürümü okur; yazanlar yine birbirini bekler.

## Sorun

Entegrasyon testlerinde aralıklı 500 görüldü (`OperationJobTests`, `POST /api/orders/orders/{id}/handover`; üç ayrı CI koşusu). Hata gövdesi yakalanınca (yerelde SQL Server ile, sipariş sınıfları paralel koşularak yeniden üretildi) neden SQL Server kilitlenmesi (1205) çıktı:

- Devir kaydı oluşurken işlem 5 varsayılan madde ekler (kümelenmiş anahtarlarda `X` kilidi) ve ardından maddeleri `WHERE TenantId = … AND HandoverId = …` ile okur. Küçük tabloda iyileştirici kümelenmiş dizini taramayı seçer; tarama başka bir tenant'ın henüz onaylanmamış satırlarında `S` kilidi bekler. İki tenant aynı anda yaptığında iki işlem birbirini bekler ve biri kurban seçilir.
- Aynı desen `platform.OutboxMessages` üzerinde de görüldü: olay dağıtıcının `TOP(n)` okuması (`S`, sayfa) ile başka isteğin eklemesi (`IX`, sayfa) çakıştı (`EventDispatcher.DispatchAsync`).

Tek paylaşımlı veritabanında (ADR-0002) bütün tenant'lar aynı sayfa ve dizinleri paylaştığı için, bir tarama başka tenant'ın kilidine takılabilir; dizin eklemek her sorguyu kapsamaz ve iyileştirici planı sabitlenemez.

## Seçenekler

| Seçenek | Artı | Eksi |
| --- | --- | --- |
| Her kilitlenme için dizin/sorgu düzeltmesi | Yerel, veritabanı ayarı gerekmez | Her yeni sorgu aynı riski taşır; plan seçimi garanti edilemez (küçük tabloda tarama seçilir) |
| İstek ya da `RunAtomicAsync` düzeyinde 1205'te yeniden deneme | Veritabanı ayarı gerekmez | İzlenen varlıkları temizlemek gerekir; yalnızca işlem sarmalayıcısını kapsar, tekil `SaveChanges` yolları açık kalır; kilitlenmeyi gizler |
| **`READ_COMMITTED_SNAPSHOT` (seçildi)** | Okuma–yazma kilitlenmesinin ve okuma beklemesinin kaynağını kaldırır; uygulama kodu değişmez | `tempdb` sürüm deposu; canlıda açarken bağlantılar kısa süre geri alınır |

## Sonuç

- `docker/sqlserver/init.sql` ayarı açar; `DatabaseSetupTests.Database_reads_committed_snapshots_so_readers_never_wait_for_writers` kapalıyken kırılır.
- Yazma–yazma çakışmaları değişmez: `sp_getapplock` (`ITransactionLock`), `rowversion` ve benzersiz dizinler gerekli yerlerde kalır.
- Doğrulama (yerel, SQL Server 2025): sipariş/devir sınıfları 52 test paralel, kapalıyken ilk turda 3 test kilitlenmeyle kırıldı; açıkken art arda 10 turda kırılma yok. CI'da tek tur kanıt sayılmaz; kırılma yeniden görülürse kilitlenme grafiği `system_health` oturumundan alınır.
- Açık: canlı veritabanında açma tarihi ve `tempdb` izleme eşiği işletimin kararıdır.
