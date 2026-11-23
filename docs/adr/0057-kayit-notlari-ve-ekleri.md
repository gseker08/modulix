# ADR-0057: Kayıt notları ve notların ekleri (dilim 1)

- **Durum:** Kabul edildi, 23 Kasım 2026 (Dilim 1 bu belgeyle birlikte teslim edilir)
- **Karar:** Formlardaki serbest metin "Notlar" alanları dosya taşıyamaz. Her kayda bağlanabilen, **ayrı kayıt** olan bir Core notu (`core.note`) eklenir; notun ekleri genel dosya deposuna (ADR-0040) nota bağlanır. Mevcut `Notes` alanları dokunulmadan kalır (geriye uyumlu); yeni "Notlar" paneli formun yanına eklenir.

## Seçenekler

| Seçenek | Artı | Eksi |
| --- | --- | --- |
| Notu taşıyan kayda dosya eklemek (mevcut `AttachmentPanel`) | Kod yok | Ek, notla değil kayıtla ilişkilidir; hangi dosya hangi nota ait belli olmaz; notu yazan ayrı izlenemez |
| Her modülde ayrı not tablosu | Modül sahipliği net | Aynı metadata, yetki ve dosya kodu her modülde tekrar edilir |
| **Core'da tek `core.note` kayıt türü (seçildi)** | Tek yetki ve doğrulama kuralı; her kayıt türü (kayıt deposu olan) not alır; ek altyapısı değişmeden çalışır (`/api/files` + `entityKey=core.note`) | Mevcut serbest metin `Notes` alanları ayrı kalır; taşıma sonraki dilimdir |

## Karar

- **Veri:** `core.RecordNotes` (`ITenantEntity` + `ICompanyShared`, RLS altında): `ParentEntityKey`, `ParentId` (oluşturulduktan sonra değişmez), `Body` (≤ 4000), `CreatedByUserId`, sürüm ve zamanlar. Bağlı kayda yabancı anahtar yoktur (kayıt deposu bağlı kaydı boru hattıyla çözer).
- **Erişim = bağlı kaydın erişimi:** bağlı kaydı (kapsam dahil, `IRecordPipeline.GetAsync`) okuyabilen notları görür; göremeyene liste boş, tekil okuma 404 döner (varlık sızmaz). Not eklemek bağlı kayıtta **güncelleme izni**, bağlı kaydın `IRecordWriteGuard` kuralı ve **yazılabilir modül lisansı** ister (403). Notu ve notun eklerini **yalnızca yazarı** değiştirir ve siler (diğerleri, yönetici dahil, 403).
- **Notun ekleri:** `POST /api/files` (`entityKey=core.note`, `recordId=<not>`); kural nota uygulanır (`IRecordWriteGuard`: yazar + bağlı kayıtta yazma). Not silinince ekleri `core.file-cleanup` aboneliğiyle (ADR-0040 K8) temizlenir. Bağlı kayıt silinirse notlar erişilemez kalır (K8'deki "olay yayınlamadan silinen alt satır" sınırı); tenant silme hepsini kapsar.
- **Uç noktalar:** genel kayıt uçları `/api/records/core.note` (liste `parentEntityKey` + `parentId` süzgeci ister; en yeni önce). Ek izin anahtarı yoktur.
- **Arayüz:** `NotesPanel` bileşeni (`ParentEntityKey`, `ParentId`, `CanWrite`): not listesi, ekleme/düzenleme/silme (`ConfirmAction`), her notun altında `AttachmentPanel`. İlk ekran: Firma ayrıntısı.
- **Modüller arası kural:** not Core'dadır; modüller `NotesPanel`'i yalnızca kendi kayıt türüyle kullanır, başka modülün tablosuna dokunmaz.

## Kapsam dışı (sonraki dilimler)

| Dilim | İçerik |
| --- | --- |
| 2 | `NotesPanel`'i `Notes` alanı olan diğer detay formlarına bağlamak (Kişi, Fırsat, Müşteri adayı, Teklif, Sözleşme, Sipariş, Lojistik, İK, Filo, Antrepo, Destek) |
| 3 | Mevcut `Notes` metinlerinin ilk not olarak taşınması ve alanın kapatılması (`migration-compat` uyumlu iki adım) |
| 4 | Not başına @anma/bildirim, sabitleme, yalnızca yazar+yönetici görünürlüğü gibi sınıflar |

## Testler

`NoteTests` (entegrasyon): mutlu yol (not ekle, listele, düzenle, nota dosya ekle, not silinince ek erişilemez), doğrulama (zorunlu alan, uzunluk, bilinmeyen tür, nota not, bağlı kayıt değişmez, bağlı kayıtsız liste), tenant izolasyonu (B, A'nın kaydına not ekleyemez; notu ve eki göremez), yetki (okuyan yazamaz, kaydı göremeyen notu görmez, yazar dışı değiştiremez/silemez/ek ekleyemez). `NoteKeysTests` (birim): istemci anahtarları sunucuyla aynı.
