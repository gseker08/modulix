# ADR-0046: Modül dikey dilimleri (uç noktalar ve arka plan işleri modüle taşınır)

- **Durum:** Kabul edildi, 14 Kasım 2026 (açık sorular aynı gün karara bağlandı; aşağıda "Açık soruların kararı"). Dilim 0 (envanter ve mimari kural), Dilim 1 (`Core.Hosting`, `IModuleEndpoints`, Antrepo pilotu), Dilim 2 (İK, Lojistik) ve Dilim 3 (iş zamanlayıcısı, Sipariş pilotu) yapıldı; Dilim 4 (kalan modül işleri) sürüyor: Dilim 4 (kalan modül işleri: Destek, teklif süre dolumu, sözleşme, pazarlama gönderimi; ortak zamanlayıcıya kuyruk modu) 15-16 Kasım 2026'da yapıldı; Dilim 5 (orta modüllerin uç noktaları) sürüyor: Sipariş ve Pazarlama yapıldı, sırada Destek.
- **Karar:** Bir modülün HTTP uç noktaları ve arka plan işleri `Modulix.Api` yerine modülün kendi projesinde durur. Host (`Modulix.Api`) modülleri tanımaz; uç noktaları ve işleri modül kaydından (`IModule` ve yanındaki arayüzler) keşfeder. Arka plan işleri modül başına ayrı `BackgroundService` yerine tek bir zamanlayıcıdan çalışır. DTO'lar ve Blazor ekranları bu ADR'de yerinde kalır; onlar için yalnızca yön belirlenir.

> Numara notu: 14 Kasım 2026'da `main`'de en yüksek numara 0045'ti ve 0046 adını taşıyan açık PR yoktu. Birleştirmeden önce numara yeniden kontrol edilmeli (0043 iki ADR'de kullanılmış durumda).

## Bağlam

ADR-0044 modüller arası sözleşmeleri `Core.Contracts`'tan çıkardı; modül bağımlılıkları artık `AllowedReferences` tablosunda okunuyor. Ama bir modülün kodu hâlâ dört projeye dağılmış durumda:

| Parça | Bugün nerede | Ölçü (14 Kasım 2026) |
|---|---|---|
| Entity, kayıt depoları, iş kuralları | `Modulix.Modules.<Ad>` | modül projesi |
| HTTP uç noktaları | `Modulix.Api/<Ad>/` | Api toplam ~15.400 satır; Crm 17, Sözleşme 8, Marketing 7 dosya |
| Arka plan işleri | `Modulix.Api/<Ad>/*Worker.cs` | 11 `BackgroundService`, 5'i modüle özel |
| DTO'lar | `Modulix.Shared/<Ad>/` | Crm 15, Platform 7, Sözleşme 6 dosya |
| Ekranlar | `Modulix.Web/Pages/<Ad>/` | Crm 37, İK 22, Antrepo 12 sayfa |

Sonuçları:

- **Host bütün modülleri tanıyor.** `Program.cs` içinde 62 uç nokta eşleme ve iş kaydı satırı var. Yeni modül eklemek host'u değiştirmek demek; "modül tek `IModule` uygulamasıyla kaydolur" kuralı yalnızca metadata için geçerli.
- **Modül sınırı Api'de delinebiliyor.** `Modulix.Api` her projeye başvurabildiği için uç nokta dosyaları başka modülün koduna ve altyapıya doğrudan uzanabiliyor. Bugün: `Api/Support` ve `Api/Contracts` `Modulix.Api.Crm`'e başvuruyor; 22 Api dosyası `ModulixDbContext`'i doğrudan kullanıyor (10'u modül klasörlerinde). Dilim 0'a kadar mimari testler bunu görmüyordu, çünkü proje başvurularına bakıyorlardı ve Api'nin her şeye başvurması serbest. (Taslak yazılırken görülen `Api/Contracts` → `Modulix.Modules.Crm` başvurusu ADR-0044 Dilim 2 ile sözleşme projesine döndü; aşağıda "Dilim 0 envanteri".)
- **Arka plan işleri kopyalanarak çoğalıyor.** Modüle özel beş iş (`QuoteExpiry`, `ContractJobs`, `OrderJobs`, `SupportJobs`, `MarketingSend`) aynı kalıbı tekrarlıyor: `WorkerEnabled` ve `PollIntervalSeconds` seçenekleri, tenant'ları gezen döngü, kapsam (scope) açma, lisans kontrolü, hata yakalama. Her yeni modül bir kopya daha ekliyor; lisans ve tenant durumu kuralları (ADR-0028) her kopyada ayrı ayrı doğru yazılmak zorunda.
- **Customs taşınamıyor.** ADR-0044 karar 3: GTİP ve rejim profili sözleşmeleri `Core.Contracts`'ta, uygulaması `Core` ve `Infrastructure`'da, uç noktaları `Api/Customs`'ta. Modüller yalnızca tenant tablolarına erişebildiği için (`IModuleData`) bu kod Warehouse modülüne taşınamıyor.

## Seçenekler

1. **Olduğu gibi bırak.** Maliyeti yok. Host her modülle büyür; Api'deki sınır ihlalleri testle yakalanamaz.
2. **Yalnızca kural ekle.** Kod yerinde kalır, mimari teste "`Api/<Ad>/` yalnızca kendi modülüne başvurur" kuralı yazılır. Ucuz ve ihlalleri durdurur; ama host modülleri tanımaya devam eder ve işlerdeki tekrar çözülmez. Ara adım olarak değerli (Dilim 0).
3. **Uç noktalar ve işler modül projesine (seçilen).** Modül projesi ASP.NET'e başvurur, uç noktalarını kendisi eşler; host yalnızca döngüyle çağırır.
4. **Modül başına ayrı uç nokta projesi** (`Modulix.Modules.<Ad>.Api`). Modülün iş kodu ASP.NET'ten bağımsız kalır. Karşılığında proje sayısı iki katına yakın artar ve uç noktalar modülün `internal` tiplerine erişemez (`InternalsVisibleTo` ya da gereksiz `public` gerekir). Bu ölçekte kazancı maliyetini karşılamıyor.
5. **Tam dikey dilim** (DTO ve ekranlar da modülde). En temiz sonuç, en büyük iş: Blazor WebAssembly'de modül başına Razor sınıf kitaplığı, ortak bileşen kitaplığı ve yönlendirme değişikliği gerekir. Bu ADR'nin kapsamı dışında bırakıldı (aşağıda "Yön").

## Karar

### 1. Modülün ASP.NET'e erişimi: `Modulix.Core.Hosting`

`Modulix.Core.Contracts` ASP.NET'e başvurmaz ve başvurmayacak (sözleşme projeleri ve testler hafif kalır). Uç nokta yazmak için gereken her şey yeni bir projeye konur:

- `Modulix.Core.Hosting`: `Microsoft.AspNetCore.App` çerçeve başvurusu + `Core.Contracts`. İçeriği bugün `Modulix.Api/Access` ve `Modulix.Api/Records` altında duran yardımcılar (yaklaşık 600 satır): `.RequirePermission(...)`, `.RequireModule(...)`, kayıt boru hattı uç nokta kalıpları.
- Modüller `Core.Contracts`, `Core.Hosting`, `Modulix.Shared` ve modül sözleşme projelerine başvurabilir. `Core` ve `Infrastructure` yasak kalır.
- Sözleşme projeleri (`Modulix.Modules.<Ad>.Contracts`) `Core.Hosting`'e başvuramaz.

### 2. Uç noktaların keşfi

`Core.Hosting`'de yeni arayüz; modül sınıfı `IModule` ile birlikte uygular:

```csharp
public interface IModuleEndpoints
{
    /// <summary>Modülün uç noktalarını eşler. Grup, modül lisans kontrolüyle (RequireModule) host tarafından sarılmış gelir.</summary>
    void MapEndpoints(IEndpointRouteBuilder routes);
}
```

- Host bütün modülleri gezer, `IModuleEndpoints` uygulayanlar için `/api/<modül anahtarı>` grubunu açar, gruba `RequireModule(<anahtar>)` uygular ve `MapEndpoints`'i çağırır. Böylece lisans ve salt okunur kuralı (ADR-0028) modülün unutabileceği bir şey olmaktan çıkar.
- İzin kontrolü (`RequirePermission`) uç nokta başına modülde kalır. Bugün korumasız uç noktayı yakalayan bir mimari test yok; Dilim 1'de eklenir (modül projesindeki her `Map*` çağrısı `RequirePermission` ile bitmeli ya da gerekçeli istisna listesinde olmalı).
- Ortak kayıt uç noktaları (`MapRecordEndpoints`: modülün `OwnedRecords` tanımından üretilen genel uçlar) host'ta kalır. Filo modülü bugün yalnızca bunları kullanıyor ve Api'de kendi klasörü yok; hedef durumun örneği odur.
- Yollar değişmez. Bugün `/api/<anahtar>` dışında yol kullanan modül uç noktası varsa taşıma diliminde listelenir ve yerinde bırakılır; bu ADR URL değiştirmez.

### 3. Tek iş zamanlayıcısı

`Core.Contracts`'a (ASP.NET gerekmez) iş tanımı eklenir; `IModule.Jobs` ile bildirilir:

```csharp
public sealed record ModuleJobDefinition(
    string Key,                 // "orders.jobs" — modül anahtarıyla başlar
    TimeSpan DefaultInterval,
    Type Runner,                // IModuleJob uygulaması, modülde
    string Identity,            // sistem kimliği: modülün bir olay aboneliği ("orders.scheduler")
    bool RequiresWritableModule = true);

public interface IModuleJob
{
    /// <summary>Tek tenant için çalışır; kapsam, tenant bağlamı ve lisans kontrolü zamanlayıcıdan gelir.</summary>
    Task<ModuleJobResult> RunAsync(ModuleJobContext context, CancellationToken cancellationToken);
}
```

(Kod bloğu Dilim 3'te yazılan hâliyle güncellendi; taslaktan sapmalar "Dilimler" bölümünde.)

- Host'ta tek `ModuleJobsWorker` bulunur: işleri aralığına göre sıraya koyar, erişime açık tenant'ları gezer (`TenantStatusRules.Operational`), her tenant için kapsam açıp tenant bağlamını kurar, modülü `IModuleLicense.AllowsAsync`/`IsWritableAsync` ile denetler, hatayı tenant ve iş bazında yalıtır ve günlüğe yazar.
- Aralık ve aç/kapa ayarı tek yapılandırma kalıbına iner: `Jobs:<anahtar>:Enabled`, `Jobs:<anahtar>:IntervalSeconds`. Eski bölüm adları (`OrderJobs:WorkerEnabled` gibi) bir sürüm boyunca okunmaya devam eder (on-prem yapılandırmaları kırılmasın).
- Entegrasyon testleri bugünkü gibi zamanlayıcıyı kapatıp işi doğrudan çağırır.
- Platform işleri (`EventDispatch`, `EmailDispatch`, `Automation`, `ApprovalEscalation`, `ReportSchedule`, `CommunicationCleanup`) bu ADR'de yerinde kalır. Tenant başına çalışan ve aynı kalıba uyanlar sonradan aynı zamanlayıcıya alınabilir; olay ve e-posta dağıtımı gibi sürekli kuyruk tüketen işler ayrı kalır.
- Hazır kitaplık (Hangfire, Quartz) kullanılmaz: ihtiyaç "aralıkla, tenant başına, tek örnekte çalıştır"dan ibaret; ek tablo, pano ve bağımlılık on-prem tek imaj hedefine yük getirir. Çok örnekli çalışma gerekirse (aynı işin iki kopyada birden koşmaması) ayrı karar alınır.

### 4. Taşımanın ön koşulu: uç nokta yalnızca modül servislerini çağırır

Bir uç nokta dosyası modüle taşınabilmek için `Core` iç koduna, `Infrastructure`'a ve başka modülün ana projesine başvurmamalıdır. Bugün başvuranlar taşınmadan önce düzeltilir:

- `ModulixDbContext` kullanan uç nokta kodu modülün kayıt depolarına ya da `IModuleData`'ya alınır.
- Başka modülün koduna uzanan uç nokta (bugün yok; `ApiModuleFolderRulesTests` yenisini engeller) o modülün sözleşme projesindeki bir arayüze çevrilir (ADR-0044).
- `Core` iç tiplerine ihtiyaç gerçekse (`Core.Events`, `Core.Platform`, `Core.Settings`) karşılığı `Core.Contracts`'a arayüz olarak çıkarılır; çıkarılamıyorsa o uç nokta host'ta kalır ve gerekçesiyle listelenir.

Bu, taşımanın asıl işidir ve modül başına ayrı ayrı yapılır. Küçük modüller (Antrepo, Lojistik, İK: uç noktaları bugün yalnızca kendi modülüne ve `Shared`'a başvuruyor) neredeyse hazırdır; Crm, Sözleşme, Marketing, Sipariş ve Destek için önce temizlik gerekir.

### 5. DTO'lar ve ekranlar: yön (bu ADR'de taşınmaz)

- **DTO'lar** `Modulix.Shared`'da kalır; modüller `Shared`'a başvurabilir hâle gelir (bugün uç noktalar Api'de olduğu için gerekmiyordu). `Shared` bağımlılıksızdır ve WebAssembly'ye derlenir; bu değişmez. Modül başına DTO projesi, ekranlar taşınırken birlikte değerlendirilir.
- **Ekranlar** `Modulix.Web`'de kalır. Modül başına Razor sınıf kitaplığı ayrı bir ADR konusudur ve şu sorular yanıtlanmadan açılmaz: ortak bileşenlerin (`LayoutForm`, `ViewTabs`, `RecordTable`) ayrı kitaplığa çıkarılması, yönlendirmede ek derlemeler, kapalı modülün ekran kodunun indirilmemesi (lazy loading) isteniyor mu.

### 6. Platform başvuru tabloları (Customs için)

Modül, tenant'sız ve tenant'lar için salt okunur **başvuru tablosu** sahibi olabilir (GTİP kataloğu, rejim profili gibi). Kurallar:

- Entity yeni bir işaret arayüzü uygular (`IPlatformReferenceEntity`); `TenantId` taşımaz, RLS dışındadır, tablo `platform` şemasında kalır (şema taşınmaz, migration gerekmez).
- Modül bu tablolara `IModuleData`'dan ayrı, yalnızca okuma veren bir arayüzle erişir. Yazma yalnızca platform uçlarından (`/api/platform`, `PlatformAccessRules`) yapılır.
- `TableInventoryTests` yeni sınıfı tanır: her tablo ya tenant tablosu, ya platform tablosu, ya da bir modülün başvuru tablosudur.
- Tenant verisi dışa aktarma ve silme (`tenant-export`, `tenant-delete`) bu tablolara dokunmaz; tenant'a ait veri içermezler.

Customs sözleşmeleri `Modulix.Modules.Warehouse.Contracts`'a, uygulaması ve uç noktaları Warehouse modülüne taşınır (ADR-0032'deki tek Warehouse modülü kararıyla uyumlu; ayrı Gümrük modülü açılmaz). ADR-0044'teki son `PendingMoves` istisnası böylece kapanır.

## Açık soruların kararı (14 Kasım 2026)

1. **`Core.Hosting`: ayrı proje.** ASP.NET yardımcıları yeni `Modulix.Core.Hosting` projesine konur. `Core.Contracts` ASP.NET'e başvurmaz; sözleşme projeleri ve birim testleri hafif kalır.
2. **Modül → `Modulix.Shared` başvurusu: kabul.** DTO'lar `Shared`'da kalır, modüller ona başvurur. Çift DTO tanımı yapılmaz; modül başına DTO projesi ekranlar taşınırken yeniden değerlendirilir.
3. **Platform başvuru tabloları: kabul, Customs Warehouse'a.** Bölüm 6'daki model geçerlidir. Ayrı Gümrük modülü açılmaz; GTİP ya da rejim profiline ihtiyaç duyan başka modül `Modulix.Modules.Warehouse.Contracts` üzerinden kullanır. Antrepo lisansı olmayan bir müşterinin GTİP'e ihtiyacı doğarsa ayrı modül o gün yeniden ele alınır.
4. **Platform ve çekirdek uç noktaları: host'ta kalır.** `Api/Platform`, `Auth`, `Users`, `Customization`, `Imports` gibi modüle ait olmayan klasörler bu ADR'de taşınmaz. Kabul ölçütündeki "host modül adı içermez" kuralı bunları kapsamaz.
5. **Çok örnekli çalışma: tek örnek varsayılır.** İş kilidi (veritabanında kira/lease) bu ADR'ye girmez; birden çok API örneği planlandığında ayrı kararla eklenir. `ModuleJobsWorker` buna engel olmayacak biçimde yazılır: işin tenant başına çalıştırılması tek bir noktadan geçer, kilit oraya eklenebilir.
6. **Api'nin Crm'e özel yardımcıları: karar değil, Dilim 0 çıktısı.** `Api/Support` ve `Api/Contracts` `Modulix.Api.Crm`'den yalnızca `LeadEndpoints.Csv` (CSV hücre kaçışı) kullanıyor; ortak yardımcı olduğu için `Core.Hosting`'e gider (aşağıda "Dilim 0 envanteri").

## Dilim 0 envanteri (14 Kasım 2026)

Api'deki modül klasörlerinin kendi modülü, `Core.Contracts`, `Shared`, modül sözleşme projeleri ve `Api/Access` ile `Api/Records` dışında başvurduğu host içi kod. Kaynak metinden çıkarıldı (`using` satırları ve tam adla yazılmış başvurular); `ApiModuleFolderRulesTests.PendingCleanup` listesiyle aynıdır ve test listeyi iki yönde denetler (yeni başvuru da, temizlenmiş ama listede kalmış satır da testi kırar).

| Klasör | Dosya | Host içi başvuru | Başvuran dosya |
|---|---|---|---|
| `Hr` | 0 | — | modüle taşındı (Dilim 2) |
| `Warehouse` | 0 | — | modüle taşındı (Dilim 1) |
| `Logistics` | 0 | — | modüle taşındı (Dilim 2) |
| `Orders` | 1 | yok (Dilim 3'te kapandı) | iş modüle taşındı |
| `Marketing` | 7 | `Core.Events`, `Core.Modules`, `Core.Platform`, `Infrastructure` | 1 (iş) |
| `Support` | 3 | `Api.Crm`, `Core.Events`, `Core.Modules`, `Core.Platform`, `Core.Records`, `Infrastructure` | 2 (biri iş) |
| `Contracts` | 8 | `Api.Crm`, `Core.Events`, `Core.Lookups`, `Core.Modules`, `Core.Platform`, `Core.Records`, `Infrastructure` | 5 (biri iş) |
| `Crm` | 17 | `Api.Users`, `Core.Events`, `Core.Modules`, `Core.Platform`, `Core.Records`, `Core.Reporting`, `Core.Settings`, `Infrastructure` | 10 (biri iş) |
| `Customs` | 3 | `Api.Imports`, `Api.Platform`, `Api.Users`, `Core.Customs` | 2 |

Okuması:

- **Başka modülün ana projesine başvuran modül klasörü yok.** Taslaktaki `Api/Contracts` → `Modulix.Modules.Crm` başvurusu ADR-0044 Dilim 2 ile sözleşme projesine (`Modulix.Modules.Crm.Contracts.Activities`) dönmüş. Kural istisnasız yazıldı.
- **Beş iş dosyası aynı dört başvuruyu taşıyor** (`Core.Events`, `Core.Modules`, `Core.Platform`, `Infrastructure`): `ContractJobsWorker`, `QuoteExpiryWorker`, `MarketingSendWorker`, `OrderJobsWorker`, `SupportJobsWorker`. Tek zamanlayıcı (Dilim 3-4) bunları kendiliğinden kaldırır; Sipariş ve Marketing'de geriye yalnızca `Core.Settings` kalır.
- **`Core.Settings` altı klasördeydi; beşinde kapandı.** En yaygın engel `ITenantSettings` idi. Dilim 1 ön koşulu olarak `Core.Contracts`'a salt okunur `ITenantSettingReader` (`Modulix.Core.Contracts.Settings`) açıldı ve modül klasörleri ona çevrildi; ayar listeleme ve yazma `ITenantSettings`'te (Core) kaldı. Crm'de `Core.Settings` başvurusu sürüyor: `SalesPlanningEndpoints` saat dilimi için `RegionalTime` kullanıyor.
- **Uç nokta dosyalarında `ModulixDbContext` kullanan dört dosya var:** `ContractSignatureEndpoints`, `LeadIntakeEndpoints`, `QuoteSharingEndpoints`, `SupportChannelEndpoints` (hepsi oturumsuz/dış kanal uçları) ve yardımcı `QuoteDelivery`. Bölüm 4'teki temizliğin asıl işi bunlardır.
- **`Api.Crm` başvurusu tek yardımcıdan ibaret (açık soru 6):** `Api/Support` ve `Api/Contracts` yalnızca `LeadEndpoints.Csv(...)` (CSV hücre kaçışı) kullanıyor. Crm davranışı değil; `Core.Hosting`'e ortak yardımcı olarak taşınır.
- **`Api.Users` başvuruları da ortak yardımcı:** Crm `PublicUrl.Resolve`, Customs `UserEndpoints.ToProblem` kullanıyor; ikisi `Core.Hosting`'e gider. Customs'ın `Api.Platform` (`PlatformEndpoints.UserId`) ve `Api.Imports` başvuruları Dilim 7'de ele alınır.
- Antrepo, İK ve Lojistik bugün taşınabilir. Sipariş ve Marketing'de iş dosyası dışında host içi başvuru kalmadı.

Sınır: denetim metin üzerindedir; üst ad alanı üzerinden kısaltılmış başvuru (`Modulix.Api.Crm` içinden `Users.Tip`) yakalanmaz. Yorum içinde geçen ad da başvuru sayılır (yanlış alarm yönünde güvenli).

## Dilimler

Her dilim ayrı PR'dır; taşıma dilimleri davranış ve URL değiştirmez.

0. **Envanter ve kural (yapıldı).** Api'deki her modül klasörü için başvuru envanteri (yukarıda). `ApiModuleFolderRulesTests`: modül klasörü başka modülün ana projesine başvuramaz (istisnasız); host içi başvurular `PendingCleanup` listesiyle sabit ve yalnızca küçülür. Kod taşınmadı.
1. **`Core.Hosting` + `IModuleEndpoints` + pilot (yapıldı).** `Modulix.Core.Hosting` projesi: `RequirePermission`, `RequireAnyPermission`, `RequireModule` (`Api/Access`'ten taşındı) ve `IModuleEndpoints`. Host `MapModuleEndpoints` ile modülleri gezer. Pilot Antrepo: pano uç noktası `Modulix.Modules.Warehouse/Endpoints`'te, `Api/Warehouse` klasörü kalktı. Taslaktan sapmalar: (a) süzgeçlerin iki host bağımlılığı sözleşmeye çevrildi: yazılabilirlik sorusu `IModuleWriteAccess` (`Core.Contracts.Modules`; `IModuleLicense` ondan türer), aktif tenant kontrolü `IActiveTenantGuard` (`Core.Hosting`; host uygular); (b) `Api/Records` yardımcıları taşınmadı, pilot onlara ihtiyaç duymuyor; ilk kullanan modülle (Dilim 2: İK, Lojistik) taşınır; (c) host'taki dosyalar süzgeçleri `global using Modulix.Core.Hosting` ile görür, tek tek değiştirilmedi. Yeni mimari test `ModuleEndpointRulesTests`: modül projesindeki her `Map*` çağrısı izin kontrolüyle biter (gerekçeli istisna listesi, bugün boş).
2. **Küçük modüller (yapıldı).** Lojistik ve İK uç noktaları modüllerine taşındı; `Api/Hr` ve `Api/Logistics` klasörleri kalktı. (Filo'nun modüle özel uç noktası yok; taşınacak bir şey çıkmaz.) Bu dilimde ortaya çıkanlar: (a) İki modülün 32 uç noktasının hiçbirinde `RequirePermission` yoktu; izin iş akışı servisinde denetleniyor (eylemin izni kayda ve duruma göre belirlenir). Davranışı değiştirmemek için `Core.Hosting`'e `PermissionCheckedInService()` işareti eklendi: süzgeç eklemez, kararı açıkça yazar. `ModuleEndpointRulesTests` uç noktada ya da grubunda izin çağrısı ya da bu işareti arar. (b) Kayıt hatasını HTTP yanıtına çeviren yardımcı `RecordProblems.ToProblem` olarak `Core.Hosting`'e alındı; host'taki `RecordEndpoints.ToProblem` ona yönlenir. `MapRecordEndpoints` kararda yazdığı gibi host'ta kaldı. (c) Zimmet tutanağı PDF'i uç noktasıyla taşındığı için İK modülü QuestPDF paketine başvuruyor (sürüm Api ile aynı). (d) `LogisticsPolicyProvider` (ayarlardan politika okuyan host köprüsü) modüle taşındı ve modülün kendi `ConfigureServices`'inde kaydoluyor; `ITenantSettingReader` sayesinde köprüye gerek kalmadı.
3. **İş zamanlayıcısı + pilot (yapıldı).** `Core.Contracts`'ta `ModuleJobDefinition`, `IModuleJob` ve `IModule.Jobs`; host'ta `Api/Jobs`: `ModuleJobRunner` (işin tek tenant için çalıştırılması: kapsam, tenant bağlamı, lisans, kilit, sistem kimliği, kayıt) ve `ModuleJobsWorker`. Pilot Sipariş: iş `Modulix.Modules.Orders/Lifecycle/OrderJobs`'ta, `Api/Orders/OrderJobsWorker` kalktı, `Api/Orders` klasörünün `PendingCleanup` satırı tamamen kapandı. Beyan açılışta doğrulanır (`ModuleCatalog`: anahtar modülle başlar, aralık pozitif, sınıf `IModuleJob`, kimlik modülün kendi aboneliği). Taslaktan sapmalar: (a) `IModuleJob.RunAsync` `ModuleJobResult` döndürür: özet metni zamanlayıcı günlüğe yazar (modül günlük kitaplığına başvurmaz), ayrıntı nesnesi testlere taşınır. (b) Tanımda `Identity` vardır: iş oturumsuz çalıştığı için izinlerini modülün bir olay aboneliğinden alır (bugünkü "orders.scheduler" kalıbı); zamanlayıcı o aboneliği sistem kimliği olarak kurar. (c) Tenant başına oturum kilidi (`sp_getapplock`) kaldırılmadı: mevcut işçide vardı ve taşıma davranış değiştirmez; kilit `ModuleJobRunner`'a taşındı, adı iş anahtarından türer (`modulix.orders.jobs:<tenant>`, eskisiyle aynı). "Çok örnekli çalışma" kararı (5) yeni bir kilit eklememek olarak okunmalı. (d) Her iş kendi döngüsünde, diğerlerinden bağımsız döner (tek `BackgroundService` içinde); ortak sıra kurulmadı, böylece uzun süren iş diğerini geciktirmez. (e) Eski yapılandırma bölümü tanımda `LegacySection` ile bildirilir (host modül adını bilmez); `Jobs:WorkerEnabled` zamanlayıcının tamamını kapatır (entegrasyon testleri bunu kullanır). (f) İşi doğrudan çağıran iki entegrasyon testi `OrderJobsRunner` sınıfını artık DI'dan değil test yardımcısından alır (birer satır); sınamalar değişmedi.
4. **Kalan modül işleri.** `QuoteExpiry`, `ContractJobs`, `SupportJobs`, `MarketingSend`. Her iş ayrı PR'dır.
   - **Destek (yapıldı, 15 Kasım 2026).** İş `Modulix.Modules.Support/Lifecycle/SupportJobs` (`support.jobs`, kimlik `support.scheduler`, varsayılan 300 sn, eski bölüm `SupportJobs`); `Api/Support/SupportJobsWorker` kalktı. Kilit adı (`modulix.support.jobs:<tenant>`) ve lisans kuralı (yazılabilir modül) eskisiyle aynıdır. `Api/Support` klasörünün `PendingCleanup` satırı değişmedi: kalan başvurular uç nokta dosyalarından gelir (Dilim 5). SLA testleri işi test yardımcısı `SupportJobsRunner` ile çağırır (birer satır).
   - **Teklif süre dolumu (yapıldı, 16 Kasım 2026).** İş `Modulix.Modules.Crm/Quotes/QuoteExpiryJob` (`crm.quote-expiry`, kimlik `crm.quote-expiry`, varsayılan 300 sn, eski bölüm `QuoteExpiry`); `Api/Crm/QuoteExpiryWorker` kalktı. Kilit adı (`modulix.crm.quote-expiry:<tenant>`) ve lisans kuralı eskisiyle aynıdır. Sapma: eski işçide en kısa aralık 10 sn idi, ortak zamanlayıcıda 30 sn'dir (30 sn'den kısa yapılandırma 30'a yükselir). İşin uyarısı (`QuoteExpiryRun.Warning`) özet metnine eklenir, zamanlayıcı bilgi düzeyinde günlüğe yazar (eskiden uyarı düzeyiydi). Testler işi `QuoteExpiryRunner` yardımcısıyla çağırır (eski imza korundu).
   - **Sözleşme işleri (yapıldı, 16 Kasım 2026).** İş `Modulix.Modules.Contracts/Lifecycle/ContractJobs` (`contracts.jobs`, kimlik `contracts.scheduler`, varsayılan 900 sn, eski bölüm `ContractJobs`); `Api/Contracts/ContractJobsWorker` kalktı. Kilit adı (`modulix.contracts.jobs:<tenant>`), lisans kuralı ve en kısa aralık (30 sn) eskisiyle aynıdır; davranış farkı yoktur. Elle tetikleme ucu (`POST /api/contracts/scheduler/run`) `IContractScheduler`'ı çağıranın kimliğiyle doğrudan çağırmaya devam eder. Testler işi `ContractJobsRunner` yardımcısıyla çağırır (eski imza korundu).
   - **Kuyruk modu (yapıldı, 16 Kasım 2026; `MarketingSend` için ön koşul).** `MarketingSend` aralıkla çalışan bir iş değil, gönderim kuyruğudur (iş varken 2 sn, yokken 15 sn; ≈76 ileti/sn ölçülmüştü); 30 sn'lik sabit aralıkla taşınsaydı gönderim hızı kabaca 17 ileti/sn'ye düşerdi. Karar (16 Kasım 2026): zamanlayıcı genişletildi. `ModuleJobDefinition.BusyInterval` işi kuyruk işi yapar; iş turunda `ModuleJobResult.HasMoreWork` bildirirse bir sonraki tur bu kısa aralıkla başlar. Kuyruk işinde alt sınırlar 5 sn (boşta) ve 1 sn (yoğun); aralıkla çalışan işlerde 30 sn değişmedi. Yapılandırma `Jobs:<anahtar>:BusyIntervalSeconds` (eski bölümde `BusyIntervalSeconds`). İşin kendi ayarları (ör. parti boyutu) `ModuleJobContext.Settings` ile verilir: eski bölümün düz değerleri, üzerine `Jobs:<anahtar>` değerleri; iş yapılandırma kitaplığına başvurmaz (`PositiveInt` yardımcısı). `ModuleJobResult.IsWarning` özetin uyarı düzeyinde yazılmasını sağlar. Katalog, yoğun aralığın pozitif ve olağan aralıktan kısa olmasını denetler.
   - **Pazarlama gönderimi (yapıldı, 16 Kasım 2026).** İş `Modulix.Modules.Marketing/Runs/MarketingSendJob` (`marketing.send`, kimlik `marketing.sender`, boşta 15 sn, yoğun 2 sn, eski bölüm `MarketingSend`); `Api/Marketing/MarketingSendWorker` kalktı ve `Api/Marketing` klasörünün `PendingCleanup` satırı tamamen kapandı. Kilit adı (`modulix.marketing.send:<tenant>`), lisans kuralı ve parti ayarları (`BatchSize`, `MaxBatchesPerRun`, `MaxRunsPerTenant`; artık `ModuleJobContext.Settings`'ten) aynıdır. Aynı hata yalnızca değişince yazılır (modülde tekil `MarketingSendErrors`; uyarı düzeyi `IsWarning` ile korunur). Sapmalar: (a) iş artık erişime açık bütün firmalarda (`TenantStatusRules.Operational`: Active ve Trial) çalışır; eskiden yalnızca Active'ti, ADR-0028 öncesinden kalma bir süzgeçti ve bilinçli bir karar bulunamadı. (b) Alt sınırlar kuyruk modununkiler (boşta 5 sn, yoğun 1 sn); eskiden aynıydı. Testler işi `MarketingSendRunner` yardımcısıyla çağırır (eski imza korundu). Böylece dilim 4 tamamlandı.
5. **Orta modüller.** Sipariş, Destek, Marketing: önce bölüm 4'teki temizlik, sonra taşıma (modül başına bir PR).
   - **Sipariş (yapıldı, 16 Kasım 2026).** Uç noktalar `Modulix.Modules.Orders/Endpoints/OrdersEndpoints`'te, `OrdersModule` `IModuleEndpoints` uygular; `Api/Orders` klasörü kalktı. URL'ler ve davranış değişmedi. Ön koşul olarak `ResourceEndpoints` (tipli CRUD kaynağı) ve iki istek yardımcısı (`RecordRequests.FiltersFrom`, `VersionRequired`) `Api/Records`'tan `Core.Hosting`'e taşındı (Dilim 1 sapma (b)'nin kapanışı); host'taki `RecordEndpoints` aynı adlarla onlara yönlenir. Bunun için `Core.Hosting` `Modulix.Shared`'a başvurur (sayfalı yanıt `PagedResult`; `Shared` hiçbir projeye başvurmadığından döngü olmaz). `OrderPolicyProvider` modülün kendi `ConfigureServices`'inde kaydolur (Lojistik'teki gibi). Sipariş modülü artık `Core.Hosting` ve `Shared`'a başvurur (kural tablosu güncellendi).
   - **Pazarlama (yapıldı, 16 Kasım 2026).** Altı dosya `Modulix.Modules.Marketing/Endpoints`'e taşındı (segment, kampanya, çalıştırma, yanıt ve rapor uçları); `MarketingModule` `IModuleEndpoints` uygular, `Api/Marketing` klasörü kalktı. URL'ler ve davranış değişmedi. `MarketingPolicyProvider` modülün kendi `ConfigureServices`'inde kaydolur. Pazarlama modülü artık `Core.Hosting` ve `Shared`'a başvurur (kural tablosu güncellendi).
   - **Destek için ön temizlik: dışa aktarma denetimi ve CSV (yapıldı, 16 Kasım 2026).** Beş liste dışa aktarımı (müşteri adayı, fırsat, teklif, sözleşme, destek talebi) denetim kaydını Core'un iç tipleriyle (`IAuditWriter`, `AuditEntry`, `IUnitOfWork`) ve CSV hücre kaçışını `Api/Crm`'deki `LeadEndpoints.Csv` ile yazıyordu. Yerine `Core.Contracts.Records.IRecordExportAudit` (uygulaması `Core.Records.RecordExportAudit`, denetim kaydının biçimi aynı: sayı, ek bilgi, süzgeç) ve `Core.Hosting.CsvText.Escape` geldi. `PendingCleanup`'tan dört satır kapandı: Destek ve Sözleşme için `Modulix.Api.Crm` ve `Modulix.Core.Records`. Destek'te kalan başvurular herkese açık anket ucundan gelir (tenant çözümü, lisans ve sistem kimliği); ortak bir sözleşmeyle ayrı adımda kapanacak.
6. **Büyük modüller.** Sözleşme, Crm (Crm birden çok PR).
7. **Platform başvuru tabloları ve Customs.** `IPlatformReferenceEntity`, salt okunur erişim arayüzü, `TableInventoryTests`; Customs Warehouse modülüne taşınır.
8. **Kapanış.** `Program.cs`'te modüle özel satır kalmadığını denetleyen mimari test; `CLAUDE.md` ve README güncellemesi ("yeni modül için host değişmez").

## Kabul ölçütleri

- `Modulix.Api` hiçbir modülün adını içermez: modül kaydı (`ModulixModules.Create()`) dışında modüle özel `using`, uç nokta eşlemesi ya da iş kaydı yoktur (mimari testle denetlenir).
- Yeni bir modül eklemek `Modulix.Api`'de modül listesine bir satır eklemekten ibarettir.
- Modül projeleri `Core` ve `Infrastructure`'a başvurmaz (bugünkü kural değişmez); `Core.Hosting` ve `Shared` başvurusu tabloda yazılıdır.
- Her modül uç noktası host tarafından `RequireModule` ile sarılır; izin kontrolü olmayan modül uç noktası (yeni) mimari testi kırar.
- Modüle özel arka plan işleri tek zamanlayıcıdan çalışır; erişime kapalı tenant ve lisansı bitmiş modül için iş çalışmaz (entegrasyon testiyle).
- Her taşıma diliminde mevcut entegrasyon testleri değişmeden geçer (yalnızca `using` satırları değişebilir).

## Sonuçlar

**Olumlu**

- Modülün sunucu tarafı tek projede toplanır; sınır ihlali derleme hatası olur (modül başka modülün ana projesine ve `Infrastructure`'a başvuramaz).
- Yeni modül host'a dokunmadan eklenir; paralel dallarda `Program.cs` çakışmaları biter.
- Lisans, tenant durumu ve hata yalıtımı kuralları işlerde tek yerde uygulanır.
- Customs için yol açılır; ADR-0044'ün son istisnası kapanabilir.

**Olumsuz**

- Modül projeleri ASP.NET'e bağlanır; modül birim testleri de bu çerçeveyi yükler (derleme süresine etkisi ölçülmedi; Api Release derlemesi bugün 17-35 dakika sürüyor).
- Büyük modüllerde (Crm, Sözleşme) taşımadan önce temizlik gerekir; iş haftalara yayılır ve açık PR'larla çakışır. Dilimler küçük tutulmalı, taşıma PR'ları hızlı birleştirilmeli.
- `InternalsVisibleTo` ile test edilen iç tipler çoğalabilir; uç nokta kodu modüle girince modülün `internal` yüzeyi büyür.
- Tek zamanlayıcı tek hata noktasıdır; iş başına yalıtım ve günlükleme doğru yazılmazsa bir modülün işi diğerlerini geciktirir.

**Kapsam dışı**

- DTO'ların ve Blazor ekranlarının modüle taşınması (ayrı ADR).
- Modül başına `DbContext` ya da migration zinciri (tek `ModulixDbContext` ve tek zincir kalır).
- Platform işlerinin (`EventDispatch`, `EmailDispatch` vb.) yeniden tasarımı.
- URL ve API sözleşmesi değişiklikleri.
