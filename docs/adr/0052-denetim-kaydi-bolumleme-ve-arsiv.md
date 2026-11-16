# ADR-0052: Denetim kaydının aylık bölümlenmesi ve arşivi

- **Durum:** Kabul edildi, 16 Kasım 2026. Kullanıcı "tarihe göre bölümleme ve arşiv" seçeneğini seçti (mimari değerlendirme A9). Sıcak saklama süresi için kullanıcı süre vermedi; varsayılan 24 ay, yapılandırmayla değişir. Dilim 1 (bölümleme) PR #445 ile main'de; dilim 2 (arşiv komutu) ayrı PR'da.
- **Karar:** `core.AuditLog` `OccurredAt` sütununa göre **aylık** bölümlenir. Sıcak saklama süresinden (varsayılan 24 ay) eski aylar tenant başına sıkıştırılmış dosya olarak nesne deposuna arşivlenir, sonra o ayın bölümü bir kerede boşaltılır (`TRUNCATE ... WITH (PARTITIONS)`). Satır satır silme yapılmaz. Arşivleme işletmenin çalıştırdığı bir komuttur (`audit-archive`); uygulama süreci denetim kaydını yine değiştiremez ve silemez.

> Numara notu: 16 Kasım 2026'da `main`'de en yüksek numara 0051'di. Birleştirmeden önce numara yeniden kontrol edilmeli.

## Bağlam

Denetim kaydı her kayıt yazmasında, her yönetim işleminde ve her yapay zekâ işleminde büyür; tek paylaşımlı veritabanında en hızlı büyüyen tablodur. Bugün temizliği yoktur: otomasyon (`AutomationRetention`), iletişim, oturum ve içe aktarma raporunun temizlik işleri vardır, `core.AuditLog`'un yoktur.

Tablonun iki koruması bu kararı sınırlar:

1. **Ekleme-yalnız.** Uygulama rolü (`modulix_app_role`) `core.AuditLog` üzerinde `UPDATE/DELETE` yapamaz (baseline migration'ındaki `DENY`). Bu, denetim kaydının uygulama hatasıyla ya da ele geçirilmiş bir uygulama kimliğiyle silinememesi içindir; korunur.
2. **Row-Level Security.** Satırlar `TenantId` ile süzülür ve engellenir. Bir tenant'ın verisi `TenantId` ile eksiksiz dışa aktarılabilir ve silinebilir olmalıdır.

Satır satır silme (`DELETE ... WHERE OccurredAt < @sınır`) büyük tabloda uzun kilit, işlem günlüğü şişmesi ve bütün tenant'lara yansıyan yavaşlık demektir. SQL Server bölümlemesi bir ayı meta veri işlemiyle boşaltır. 16 Kasım 2026'da yerel SQL Server 2025'te denendi: RLS politikası açık, bölümlenmiş tabloda `TRUNCATE TABLE ... WITH (PARTITIONS (n))`, `SWITCH PARTITION`, `MERGE RANGE` ve `SPLIT RANGE` çalışıyor.

Tablo bugün küçüktür. Bölümleme için kümelenmiş dizin yeniden kurulur; bunun en ucuz anı şimdidir.

## Seçenekler

1. **Süresiz saklama.** Kod değişmez; tablo ve yedek sınırsız büyür, eski satırlara erişim hızı düşer. KVKK'nın "gerekli olandan uzun saklamama" ilkesiyle de çelişir. Reddedildi.
2. **Satır satır silen temizlik işi (diğer temizlikler gibi).** Uygulama rolüne `DELETE` izni vermeyi gerektirir (ekleme-yalnız güvencesi kalkar) ve büyük tabloda pahalıdır. Reddedildi.
3. **Aylık bölümleme + nesne deposuna arşiv + bölüm boşaltma (seçildi).** Ekleme-yalnız güvencesi korunur; silme işletmen kimliğiyle (migrator) ve meta veri işlemiyle yapılır; eski kayıt kaybolmaz, arşivde durur.
4. **Ayrı arşiv veritabanı.** Arşiv sorgulanabilir kalır ama ikinci veritabanı, ikinci yedek ve ikinci RLS kurulumu demektir; on-prem kurulumu ağırlaştırır. Gerekirse 3'ün üstüne eklenebilir. Ertelendi.

## Ayrıntılar

### Bölümleme (dilim 1)

- Bölüm işlevi `AuditLogMonthly` (`datetimeoffset(7)`, `RANGE RIGHT`), sınırlar her ayın ilk günü 00:00 UTC. Bölüm şeması `AuditLogMonthlyScheme` bütün bölümleri `PRIMARY` dosya grubuna koyar (on-prem'de ek dosya grubu gerekmez).
- İlk sınırlar Ocak 2026'dan Aralık 2030'a kadar migration'da açılır (60 sınır). Daha sonraki aylar için sınırları arşiv komutu açar (dilim 2). Komut hiç çalıştırılmasa bile yazma bozulmaz: Aralık 2030'dan sonraki satırlar son bölüme düşer.
- Birincil anahtar `(OccurredAt, Id)` olur ve kümelenmiştir. Bölümlenmiş tabloda benzersiz dizinler bölüm sütununu içermek zorundadır. Zaman sıralı kümelenmiş anahtar yeni satırları tablonun sonuna ekler. `Id` tek başına benzersiz olarak zorlanmaz; değeri `Guid.CreateVersion7()` ya da rastgele `Guid`'dir ve hiçbir yerde tek başına anahtar olarak aranmaz.
- Bütün dizinler bölüm şemasına hizalıdır. Hizalanmamış bir dizin bölüm boşaltmayı engeller. EF modelinde bölüm şeması yoktur; bu yüzden bir entegrasyon testi `core.AuditLog`'un bütün dizinlerinin bölüm şemasında olduğunu denetler. Bu tabloya dizin ekleyen migration dizini şemaya hizalamalıdır (`AuditPartitionSql`).
- Geriye uyum: önceki sürümün kodu yalnızca `INSERT` ve `SELECT` yapar; anahtar değişikliği onu etkilemez (`migration-compat` job'u bunu sınar). Migration kümelenmiş dizini yeniden kurduğu için tablo büyükken bakım penceresinde çalıştırılmalıdır; bugün tablo küçüktür.

### Arşiv (dilim 2)

- İşletmen komutu: `dotnet run --project src/Modulix.Api -- audit-archive` (zamanlanmış görev olarak ayda bir). Migrator bağlantısıyla çalışır, çünkü bölüm boşaltma `ALTER` yetkisi ister ve uygulama rolünde bu yetki yoktur.
- Komutun adımları:
  1. Sıcak saklama süresinden (`AuditArchive:HotMonths`, varsayılan 24) eski, dolu her bölümü seçer: üst sınırı bu ayın başından `HotMonths` ay önceye eşit ya da daha eski olan bölümler. Arşiv birimi bölümdür. Eski boş sınırlar birleştirildiği için ilk bölümün alt sınırı yoktur (`PeriodStart` en küçük tarihtir).
  2. Her tenant için (`platform.Tenants`), o tenant'ın oturum değeriyle dönemin satırlarını okur ve gzip'li JSON Lines dosyası olarak nesne deposuna yazar. Satırlar RLS altında okunur; bir tenant'ın dosyasına başka tenant'ın satırı giremez.
  3. Dosyanın SHA-256 özetini, boyutunu, satır sayısını ve anahtarını `core.AuditArchives` tablosuna yazar (tenant tablosu, RLS). Tekrar çalışmada satır yenilenir, eski dosya silinir.
  4. Dosyalara yazılan satırların toplamı bölümün satır sayısını (`sys.dm_db_partition_stats`) tutarsa bölümü boşaltır. Tutmazsa bölüme dokunmaz; örneğin tenant kaydı olmayan bir satır varsa böyle olur. Komut hata koduyla biter ve durumu günlüğe yazar.
  5. Yalnızca iki yanı da boş olan eski sınırları birleştirir. Böylece veri taşınmaz ve bölüm sayısı büyümez.
  6. İleriye `AuditArchive:FutureMonths` (varsayılan 24) ay sınır açık kalacak şekilde yeni sınır ekler.
- Aynı anda tek çalışma olur (`sp_getapplock`, oturum kilidi).
- Yetim içerik temizliği (`FileCleanup.SweepAsync`) `StoredFiles`'ta kaydı olmayan dosyaları siler. Arşiv dosyalarını silmemesi için `core.AuditArchives` anahtarları da bilinen sayılır. Uygulama rolü `core.AuditArchives` üzerinde yalnızca okuma yapabilir; ekleme, değiştirme ve silme reddedilir. Aksi halde bir satırı silmek o dosyayı temizliğe açardı.
- Komut tekrar çalıştırılırsa güvenlidir: bölüm boşaltılmadan önce bir hata olursa ay olduğu gibi kalır ve bir sonraki çalışmada baştan arşivlenir.
- Arşiv dosyası uygulama düzeyinde şifrelenmez. Data Protection anahtarları dönüşümlüdür ve yıllarca saklanacak dosyayı anahtar halkasına bağlamak riskli olur. Nesne deposunun sunucu tarafı şifrelemesi ve erişim ilkesi işletme koşuludur (`docs/tenant-isletim.md`). Bütünlük veritabanındaki özetle denetlenir.

### Erişim ve tenant verisi (dilim 3)

- Denetim kaydı ekranı sıcak dönemi gösterir. Daha eski aylar için firma yöneticisi arşiv dosyasını indirir (`audit.read` izni ve denetim kaydı yazılır). Arşiv içinde arama yoktur.
- `tenant-export` ve `tenant-delete` arşivi zaten kapsar (dilim 2'de görüldü): arşiv dosyaları tenant'ın depo kökündedir ve iki komut kökün tamamını kopyalar ya da siler. `core.AuditArchives` satırları da `TenantId` ile dışa aktarılır ve silinir.

### Arşivin saklanması (dilim 4)

- Arşiv dosyaları `AuditArchive:ArchiveYears` (varsayılan 10) yıl saklanır, sonra silinir. 10 yıl, ticari defter ve belgelerin saklama süresine göre seçilmiş bir varsayılandır. **Mevzuat doğrulanmadı;** sözleşme ve KVKK aydınlatma metniyle birlikte hukuk onayı gerekir.

## Sonuçlar

- Ekleme-yalnız güvencesi korunur; silme yalnızca işletmen kimliğiyle ve meta veri işlemiyle olur.
- Sıcak tablo en fazla `HotMonths` ay + bir ay büyür; bir ayı boşaltmanın maliyeti satır sayısından bağımsızdır.
- 24 aydan eski denetim kaydı ekranda aranamaz, yalnızca indirilebilir.
- Yeni bir işletme görevi doğar: arşiv komutu ayda bir çalıştırılır. Çalıştırılmazsa yalnızca tablo büyür, veri kaybı olmaz.
- Bu tabloya dizin eklemek artık ek dikkat ister (hizalama; testle denetlenir).
