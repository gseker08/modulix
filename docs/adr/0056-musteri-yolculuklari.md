# ADR-0056: Müşteri yolculukları ve ortak akış modeli

- **Durum:** Kabul edildi (23 Kasım 2026; taslak #567 birleşti, öneriler geçerli). Uygulama durumu en altta.
- **Kaynak:** `docs/crm-eksik-surecler-epicleri.md` E4 (müşteri yolculukları) ve E6 (görsel süreç tasarımcısı); `docs/oncelikli-isler.md` A3-R5.
- **İlgili kararlar:** ADR-0027 (kural ve iş akışı altyapısı), ADR-0025 (iletişim izni, sıklık, çıkış, konu), ADR-0044 (modül sözleşme projeleri), ADR-0046 (modül zamanlanmış işi), ADR-0049 (belge numaralama).

## Problem

Modulix'te segment ve tek seferlik kampanya çalıştırması var. Zamanla ilerleyen çok adımlı bir yolculuk yok: kişi bir koşulla girer
(segmente girme, form doldurma, aday oluşma), adımlardan geçer (ileti gönder, bekle, koşula göre dallan, görev aç, sahibine bildir)
ve çıkış koşuluyla (abonelikten çıkma, fırsata dönüşme) her adımda çıkarılır. Aynı kişi aynı yolculuğa aynı anda bir kez girer; her
gönderim izin denetiminden geçer.

## Mevcut zemin (kodda doğrulandı)

- **Akış motoru ADR-0027'de hazır:** kalıcı adımlar (`AutomationRunSteps`), bekleme (`core.flow.wait`, 4c-4), adım koşuluyla dallanma
  (`AutomationActionStep.When`, 4c-5), akışı bitirme (`core.flow.stop`), onay bekleme, sürümlenmiş kural (`AutomationRuleVersions`:
  yayınlanmış sürüm değişmez; çalışan örnek başlatıldığı sürümle biter), tekilleştirme anahtarı, kiralı çalışma, deneme ve iptal.
- **Eksik olan:** (a) kişiyi yolculuğa **kaydeden** model ve "aynı anda bir kez" kuralı, (b) pazarlama iletisi gönderen akış aksiyonu,
  (c) segmente girişi algılayan tetikleyici, (d) yolculuk düzeyinde çıkış koşulu, (e) yolculuk raporu.
- **Pazarlama zemini:** `IMessageProvider` (`Modulix.Modules.Marketing.Contracts`) ve `fake` sağlayıcı, `ICommunicationPreferences`
  (izin kararı), `IFrequencyLimiter`, `IOptOutRecorder`, abonelikten çıkma bağlantısı, `ISegmentEngine`, iletişim konuları.
  Gerçek sağlayıcı ve İYS epik E3'tedir (hukuk ve sağlayıcı kararı bekliyor); yolculuk `fake` ile önceden yapılır.

## Karar önerisi

1. **Ayrı yolculuk motoru kurulmaz.** Yolculuk, ADR-0027 kural sürümünün bir **kullanım biçimidir**: adımlar kuralın aksiyon
   adımlarıdır (bekle, dallan, bitir, bildir, görev aç mevcut; "yolculuk iletisi gönder" yeni). ADR-0027 4c-4'teki gerekçe aynen geçerlidir:
   ikinci bir yürütme, tekilleştirme ve izleme yolu risk getirir, kullanıcıya fazladan bir şey vermez.
2. **Yolculuk kaydı Pazarlama modülünündür** (`marketing.Journeys`): ad, açıklama, hedef türü (`Person`/`Company`/`Lead`), giriş kaynağı,
   yeniden giriş politikası, çıkış koşulu, durum (`Draft`/`Active`/`Paused`/`Archived`) ve bağlı kural kimliği. Kural sürümü
   (adımlar) Core'dadır; yolculuk kaydı kuralı yönetir. Pazarlama Core'un kural servisini kullanır, Core pazarlamayı tanımaz.
3. **Kayıt (enrollment) tablosu:** `marketing.JourneyEnrollments` (JourneyId, hedef türü ve kimliği, kural sürümü kimliği, `Status`:
   `Active`/`Completed`/`Exited`, `EnteredAt`, `ExitedAt`, `ExitReason`, otomasyon çalışması kimliği). "Aynı anda bir kez" kuralı
   `UNIQUE(TenantId, JourneyId, TargetId) WHERE Status = 'Active'` filtreli benzersiz indeksle sağlanır. Yeniden giriş politikası:
   `Never` (varsayılan), `AfterExit` (çıkıştan sonra), `AfterCompletion` (tamamlandıktan sonra).
4. **Giriş (dilim 1):** segmente girme. Yayımlanmış dinamik segment, planlı işte (`IModuleJob`, ADR-0046) yenilenir; önceki üyelerle fark,
   **yeni girenler** için kayıt açar ve `marketing.journey.entered` iş olayını yayınlar (DomainEvent tetikleyicisi; kural bu olayla
   çalışır). Form doldurma ve aday oluşması sonraki dilimde, aynı olay yoluyla gelir (kaynak başka, sözleşme aynı).
5. **İleti gönderme aksiyonu:** `marketing.journey.send` (Pazarlama kaydeder): içerik sürümü ve kanal parametresi; her gönderimde
   izin kararı (`ICommunicationPreferences.EvaluateManyAsync`, konu dahil), sıklık sınırı, çıkış kaydı ve abonelikten çıkma bağlantısı
   kampanya çalıştırmasıyla **aynı kurallarla** uygulanır (kural kopyalanmaz; ortak servis kullanılır). Reddedilen ileti hata değil
   `Skipped` + gerekçe kodudur ve yolculuk sürer. Dilim 1-2'de yalnızca `fake` sağlayıcı.
6. **Çıkış koşulu yolculuk düzeyindedir** (`ConditionNode`): her adımdan önce değerlendirilir; sağlanırsa kayıt `Exited` olur, kalan adımlar
   atlanır. Abonelikten çıkma (pazarlama ya da tüm kanallar) koşulsuz çıkıştır. Adım koşulu (`When`) dallanma içindir, çıkış için değil.
7. **Yetki ve denetim:** izinler `marketing.journeys.read|write|publish`; yayın, kuralın "yayınlayanın izin altkümesi" kimliğiyle (ADR-0027)
   çalışır. Yolculuk yayını, duraklatma ve kayıtların elle çıkarılması denetim kaydına yazılır. Kayıt hedefi kişi/firma olduğundan
   veri kapsamı okumada uygulanır; tenant izolasyonu `TenantId` + RLS.
8. **Ortak akış modeli (E6):** görsel tasarımcı **ayrı model değildir**; aynı kural sürümü JSON'unu (tetikleyici → koşul → adımlar) çizer.
   Dilim 1: salt okunur akış çizimi (hem otomasyon kuralı hem yolculuk için); sonra düzenleme; sonra sürüm ve deneme çalıştırması (`/preview`).

## Dilimler

| Dilim | İçerik | Karar gerekir mi |
|-------|--------|------------------|
| 1 | `marketing.Journeys`, `marketing.JourneyEnrollments` (migration + RLS), yolculuk kaydı uçları, segment girişi ve `marketing.journey.entered` olayı, mimari/birim/entegrasyon testleri | Hayır (bu ADR'nin onayı) |
| 2 | `marketing.journey.send` (fake), çıkış koşulu, bekle/bitir ile uçtan uca yolculuk, kayıt listesi ve elle çıkarma | Hayır |
| 3 | Dallanma: yanıt/açıldı/dönüşüm koşulları (yanıt kaydı `CampaignResponse` mevcut; "açtı" bilgisi gerçek sağlayıcı webhook'u ister, E3) | Kısmen (E3) |
| 4 | Yolculuk raporu: adım dönüşümü, çıkış nedenleri | Hayır |
| E6-1 | Kuralı/yolculuğu salt okunur akış olarak çizme | Hayır |

## Açık kararlar (öneriyle ilerlenir)

1. **Yolculuk kaydı Core'da mı Pazarlama'da mı?** Öneri: Pazarlama (iletişim izni, segment ve içerik oradadır; Core sektörden bağımsız kalır).
2. **Segmente giriş nasıl algılanır?** Öneri: planlı yenileme + üye farkı (basit, mevcut `ISegmentEngine`'i kullanır; gecikme tarama aralığı kadardır).
   Alternatif: kayıt değişikliği olaylarıyla anlık değerlendirme (karmaşık, maliyetli).
3. **Hedef kişi mi firma mı?** Öneri: yolculuk hedef türüne bağlıdır (segmentle aynı); ileti çözümlemesi kampanyadaki alıcı çözümleyiciyle yapılır.
4. **Bekleme iş günü takvimi:** ADR-0027 4d'de takvim ortaklaştırması bekliyor; dilim 2'de takvim günü, iş günü sonra.
5. **Kayıt saklama süresi:** otomasyon çalışması saklamasıyla (90/180 gün) uyumlu; kayıt satırı yolculuk raporu için daha uzun tutulabilir (E5 saklama politikası kararı bekliyor).
6. **Eşzamanlı yolculuklar:** bir kişi birden çok yolculukta olabilir; ortak sıklık sınırı (`IFrequencyLimiter`) toplam baskıyı sınırlar.

## Bilerek yapılmayacaklar

Paralel kol, döngü, iç içe blok (ADR-0027 4c-5 ile aynı gerekçe), gerçek sağlayıcı ve İYS (E3), yapay zekâ ile yolculuk önerisi (E7),
yolculuklar arası geçiş (bir yolculuğun sonunda diğerine alma; ilk sürümde elle ya da olayla).

## Testler (her dilimde)

Mutlu yol, hata/doğrulama (geçersiz çıkış koşulu, yayınsız yolculuğa kayıt, çift giriş), tenant izolasyonu (A'nın yolculuğu ve kayıtları B'de yok;
farklı tenant aynı hedefi bağımsız kaydeder), yetki (yazma/yayın izni olmayan yolculuk açamaz/yayınlayamaz; kapsam dışı kayıt görünmez).
Mimari: yeni tablo `TableInventoryTests` ve RLS politikasına eklenir; Pazarlama yalnızca Core.Contracts ve sözleşme projelerine başvurur.
Entegrasyon testlerini yerelde SQL Server olmadığı için CI koşar; bu açıkça belirtilir.

## Sonuç

Onaylanırsa dilim 1 (model + segment girişi) ayrı PR olarak açılır. Migration içeren ilk dilimdir; `migration-compat.yml` koşar. Karar gerektiren tek
konu yukarıdaki açık karar listesidir; öneriler değişirse dilim 1'in kapsamı değişir.

## Uygulama durumu

### Dilim 1a: model ve kayıt (23 Kasım 2026)

- **Tablolar:** `marketing.Journeys` (kod, ad, hedef türü, giriş segmenti, yeniden giriş politikası, çıkış koşulu, sorumlu, durum) ve `marketing.JourneyEnrollments` (hedef, durum, giriş/çıkış zamanı, çıkış nedeni); migration `MarketingJourneys` (yeni tablolar, geriye uyumlu; `TenantIsolationSql.AddTable` ile RLS). "Aynı anda bir kez" kuralı `UX_JourneyEnrollments_Active` filtreli benzersiz indeksindedir (`WHERE Status = 'Active'`).
- **Kayıt depoları:** `JourneyRecordStore` (kod 2-40 küçük harf/rakam/tire ve tenant içinde tekil, kod ve hedef türü değişmez, giriş segmenti etkin ve aynı hedef türünde olmalı, taslak dışında segment değişmez, çıkış koşulu JSON nesnesi, yalnızca taslak ve kaydı olmayan yolculuk silinir), `JourneyEnrollmentRecordStore` (elle oluşturulamaz, değiştirilemez, silinemez; iş akışı kapısı açıkken yazılır; görünürlük yolculuğun sorumlusuna bağlı).
- **Uçlar:** `/api/marketing/journeys`, `/api/marketing/journey-enrollments` (`ResourceEndpoints.MapResource`); izinler `marketing.journeys.read|write` (rol şablonlarına eklendi: uzman ve yönetici okur/yazar, izleyici okur); denetim kaydı `AuditedEntities`'te.
- **İletiler:** `marketing.server.journey.*` ve `marketing.server.notfound.journey` (tr/en/az; en/az taslak).
- **Testler:** `JourneyTests` (entegrasyon, 4 tür: mutlu yol, hata/doğrulama, tenant izolasyonu, yetki; SQL Server gerektirir, CI koşar), `MarketingMessagesTests` eşiği 176.
- **Bilerek yapılmadı:** durum geçişleri (yayın, duraklatma, arşiv) ve yayın izni (dilim 1b'de yapıldı), segment girişi ve `marketing.journey.entered` olayı (dilim 1b'nin ikinci yarısı; aşağıdaki açık karar), çıkış koşulunun alan doğrulaması (dilim 2), adım aksiyonları, arayüz ekranı.

### Dilim 1b-1: durum geçişleri (23 Kasım 2026)

- **Servis ve uçlar:** `IJourneyService` (`Journeys/JourneyService.cs`) ve `POST /api/marketing/journeys/{id}/publish|pause|resume|archive` (gövde `{ version }`). Geçişler: Taslak→Etkin (yayın), Etkin→Duraklatılmış, Duraklatılmış→Etkin (sürdürme), Taslak/Etkin/Duraklatılmış→Arşiv (geri alınmaz). Uygun olmayan durumdan geçiş 409; yayın ve sürdürmede giriş segmenti etkin olmalıdır (değilse 409). Durum yalnızca bu servisin açtığı iş akışı kapısıyla yazılır; kayıt boru hattı izin, veri kapsamı, eşzamanlılık (sürüm) ve denetim kaydını sağlar.
- **İzin:** `marketing.journeys.publish` (yayın, duraklatma, sürdürme, arşiv); yalnızca "Pazarlama yöneticisi" rol şablonundadır (uzman yazar ama yayımlayamaz, segmentteki kuralla aynı).
- **İletiler:** `marketing.server.journey.*` altında 5 yeni ileti (tr/en/az; en/az taslak) ve izin için `meta.permission.*` anahtarları.
- **Testler:** `JourneyTests` (entegrasyon, SQL Server gerektirir, CI koşar): mutlu yol (dört geçiş), hata (uygun olmayan geçişler, eski sürüm, etkin olmayan segment, olmayan kayıt), tenant izolasyonu, yetki (yazma izni yayın izni değildir, izleyici, "kendi" kapsamı).
- **Segment girişi (dilim 1b-2) aşağıda; karar 1 uygulandı.**

### Dilim 1b-2: segment girişi ve `marketing.journey.entered` (23 Kasım 2026)

- **Karar (1, yayınlayanın izin altkümesi):** iş kimliğinin izinleri yalnızca kendi modülünün anahtarlarından oluşabildiği için, segmenti değerlendirecek okuma izinleri yolculuğu **etkinleştirenden** (yayın ya da sürdürme) alınır. Çekirdekte genel sözleşme `IGrantedExecution` (`Core.Contracts.Modules`) eklendi: iş parçasını işin kimliğiyle ama verilen izin kümesiyle ayrı tenant kapsamında çalıştırır (otomasyon çalıştırıcısının `EventHandlingScope.Begin(..., grantedPermissions)` mekanizmasının işlere açılması; uygulama `Api/Jobs/GrantedExecution`). Kural (`GrantedPermissionRules`): işin aboneliğinde bildirilmeyen her izin okuma izni (`.read`) olmalıdır; başka modülün yazma izni verilemez (ArgumentException). Yetki yükseltme yok.
- **Saklama:** `marketing.Journeys.EntryPermissions` (nullable JSON dizi; migration `MarketingJourneyEntryPermissions`, geriye uyumlu ek sütun). Yolculuk etkinleştirilirken kayıt deposu, etkinleştirenin yalnızca `.read` izinlerini yazar; kayıt alanı değildir (istekle yazılmaz, yanıtta görünmez).
- **İş:** `marketing.journey-entry` (`JourneyEntryJob`, kimlik `marketing.journeys`: segments.read, journeys.read, journeys.write; varsayılan 5 dk, iş varken 5 sn). Her etkin yolculuk için `IJourneyEntry.EnterAsync`: giriş segmentinin güncel üyeleri (`ISegmentService.MemberIdsAsync`, en çok 20 000) içinden yolculukta daha önce kaydı olmayanlar için, hedef başına tek işlemde (`RunAtomicAsync`) yolculuk kaydı (`Active`) açılır ve `marketing.journey.entered` olayı yayınlanır (kayıt: yolculuk kaydı; alanlar: journeyId, targetId, enteredAt, target). Tur başına en çok `MaxEntriesPerJourney` (500) giriş; kalan varsa `HasMoreWork`. Çakışma (başka işleyici önce girdi) atlanır; diğer hata turu durdurur ve günlüğe (yalnızca değişince) uyarı yazar.
- **Davranış:** aynı hedef bir yolculuğa bir kez girer (yeniden giriş politikası çıkış ve tamamlanma geldiğinde, dilim 2'de uygulanır). Taslak, duraklatılmış ve arşivlenmiş yolculuk girmez; segment arşivlenirse giriş uyarıyla durur, mevcut kayıtlar korunur. Etkinleştirenin CRM okuma izni yoksa segment alanı kullanılamaz sayılır ve giriş atlanır (sessiz yetki genişlemesi yok). Giriş tenant genelidir (iş kimliğiyle aynı kapsam): veri kapsamı "kendi/takım" olan bir etkinleştirenin yolculuğu da segmentin tüm üyelerini kaydeder; bu, segmentin kendi tanımının sonucudur.
- **Testler:** `JourneyEntryTests` (entegrasyon, CI koşar): mutlu yol (girişler, olay, tekrarda yeni kayıt yok, sonradan giren), hata (taslak/duraklatılmış/arşivli, arşivli segment), tenant izolasyonu, yetki (CRM okuyamayan etkinleştiren, yazma izni verilemez); `GrantedPermissionRulesTests` (birim).
- **Bilerek yapılmadı:** tur sınırının (`MaxEntriesPerJourney`) entegrasyon testi (yapılandırma tenant başına değişmediği için), otomasyon kuralı tetikleyicisinin bağlanması ve ileti gönderme (dilim 2), yeniden giriş ve çıkış (dilim 2), arayüz.
