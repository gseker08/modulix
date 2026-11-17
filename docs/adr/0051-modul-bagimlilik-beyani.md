# ADR-0051: Modül bağımlılık beyanı

- **Durum:** Kabul edildi, 16 Kasım 2026 (açık sorular aynı gün karara bağlandı, aşağıda). Dilim 1 (beyan, katalog doğrulaması, ilk beyanlar, mimari test) ve dilim 2 (zorunlu bağımlılığın hak yazılırken denetimi) teslim edildi. Dilim 3a (platform: bağımlılık bilgisi, eksik bağımlılık uyarısı, rapor) ve dilim 3b (firma yöneticisine bilgi) teslim edildi.
- **Kaynak:** [Core mimari değerlendirmesi](../core-mimari-degerlendirme-2026-11.md) H7; [öncelik listesi](../oncelikli-isler.md) C6.
- **İlgili:** ADR-0028 (lisans ve tenant yönetimi), ADR-0044 (modül sözleşme projeleri).

## Bağlam

Bir modülün başka bir modüle ihtiyaç duyup duymadığı bugün hiçbir yerde beyan edilmiyor:

- `IModule`'da bağımlılık üyesi yok. `RequiredModules` yalnızca otomasyon aksiyonu düzeyinde var (`AutomationActionDefinition`).
- ADR-0044 derleme zamanı bağımlılığını görünür kıldı (`ProjectReferenceRulesTests.AllowedReferences`), ama bu tablo "hangi proje hangisine
  başvurabilir" sorusunu yanıtlar; "tenant'ta hangi modül hangisi olmadan anlamlı çalışmaz" sorusunu yanıtlamaz.
- Modül hakkı yazan hiçbir yol bağımlılığa bakmıyor: platformdan tek modül açma/kapama (`PlatformAdministration.SetModuleAsync`), firma
  oluşturma, plan tanımı ve plan atama (`PlanAdministration`), platform yöneticisinin sektör profili uygulaması (`enableModules: true`).

Bugünkü modüller arası başvurular (csproj ve `using` taramasıyla):

| Kullanan | Sağlayan | Ne için | Sağlayan kapalıyken bugün |
|---|---|---|---|
| Sipariş | CRM | Kabul edilmiş tekliften sipariş (`IAcceptedQuoteSource`), takip aktivitesi (`IFollowUpActivities`) | Tekliften dönüşüm yok, aktivite `Unavailable`; sipariş elle açılır |
| Sözleşme | CRM | Kabul edilmiş tekliften sözleşme taslağı, takip aktivitesi | Aynı |
| Pazarlama | CRM | Alıcı çözümleme, segment alanları, yanıttan müşteri adayı (genişleme noktalarını yalnızca CRM uygular) | Alıcı ve segment alanı kaynağı kalmaz |
| Lojistik | Filo | Seferde araç ve dorse (`IVehicleDirectory`) | `IsAvailableAsync` yanlış döner; araç seçilemez |

Çalışma zamanı davranışı ADR-0044 ile sabit: sağlayan modül kapalıysa çağıran `Unavailable` alır, hata oluşmaz. Eksik olan, bu durumun
satış ve lisans tarafında bilinmesi: platform yöneticisi Pazarlama'yı CRM'siz açabiliyor ve ortaya çalışmayan bir modül çıkıyor; CRM'i
kapatırken hangi modüllerin etkileneceğini göremiyor.

## Karar

### 1. Beyan

`IModule`'a varsayılanı boş bir üye eklenir (`Modulix.Core.Contracts.Modules`):

```csharp
IReadOnlyList<ModuleDependency> Dependencies => [];

public sealed record ModuleDependency(string ModuleKey, ModuleDependencyKind Kind, string Reason);

public enum ModuleDependencyKind
{
    /// <summary>Sağlayan modül olmadan bu modül açılamaz.</summary>
    Required = 1,

    /// <summary>Sağlayan modül olmadan çalışır; <see cref="ModuleDependency.Reason"/>'daki özellik eksik kalır.</summary>
    Optional = 2,
}
```

`Reason` arayüzde gösterilen Türkçe cümledir (ör. "Kabul edilmiş tekliften sipariş oluşturma"). Beyan kodda sabittir; firma ya da platform
yöneticisi değiştiremez (bağımlılık kodun özelliğidir, yapılandırma değildir).

Açılışta `ModuleCatalog` doğrular: biçimi bozuk modül anahtarı, modülün kendisi, `core` (her zaman açıktır, beyan edilmez), boş gerekçe,
aynı modüle iki beyan ve `Required` bağımlılıklarda döngü açılışı durdurur. Sağlayan modülün katalogda bulunmaması açılış hatası değildir
(dilim 1'de görüldü: entegrasyon testleri kataloğu modüllerin alt kümesiyle kurar, ör. CRM'siz Pazarlama); beyanın gerçek bir modülü
gösterdiğini mimari test denetler.

### 2. Beyan ile proje başvurusu eşleşir

Mimari teste kural eklenir: bir modül projesi başka bir modülün sözleşme projesine başvuruyorsa, bu iki modül arasında bir beyan
bulunmalıdır. Yön serbesttir, çünkü ADR-0044'teki genişleme noktalarında başvuran taraf sağlayandır: CRM `Marketing.Contracts`'a başvurur
ama bağımlı olan Pazarlama'dır. Böylece yeni bir modüller arası başvuru beyansız eklenemez.

### 3. Hak yazılırken denetim

Kural tek yerde durur (`ModuleDependencyRules`, `Modulix.Core/Modules`): verilen açık modül kümesi için eksik zorunlu bağımlılıkları ve
eksik isteğe bağlı bağımlılıkları döndürür. Hak yazan her yol onu çağırır:

| İşlem | `Required` eksikse | `Optional` eksikse |
|---|---|---|
| Modül açma (platform) | Reddedilir; ileti eksik modülü adıyla söyler | Açılır; yanıtta uyarı döner |
| Modül kapama (platform) | Açık bir modül buna zorunlu bağımlıysa reddedilir; bağımlılar listelenir | Kapanır; yanıtta etkilenen özellikler döner |
| Firma oluşturma | Modül listesi kapalı küme değilse reddedilir | Uyarı |
| Plan tanımı ve güncelleme | Planın modül kümesi kapalı küme değilse reddedilir | Uyarı |
| Plan atama | Plan dışı açık haklarla birlikte sonuç küme denetlenir | Uyarı |
| Sektör profili (`enableModules: true`) | Profilin açacağı modüller kapalı küme olmalı | Uyarı |

Zorunlu bağımlılık kendiliğinden açılmaz: modül hakkı ticari bir karardır (kaynak, bitiş, gerekçe taşır), platform yöneticisi bilerek
açar. Reddetme 400 ve alan hatasıyla döner; denetim kaydı değişmez (başarısız işlem yazılmaz).

Dilim 2'de netleşenler:

- **Yalnızca değişikliğin yarattığı ihlal reddedilir.** Önceden var olan ihlal (karar 4) ilgisiz bir hak değişikliğini engellemez; ör.
  CRM'siz açık Pazarlama'sı olan firmada Destek yine açılır.
- **Plan uygulaması toplu denetlenir.** Plan atama ve plan güncelleme, firmada oluşacak sonuç kümesini yazmadan önce denetler; modüller
  tek tek açılıp kapanırken ara durumlar denetlenmez (`SetModuleAsync(..., checkDependencies: false)`). Plan güncellemesi geçerli
  abonelerden birini bozacaksa plan kaydedilmez.
- **Denetim platform katmanındadır**, `IModuleEntitlements.SetEnabledAsync` düzeyinde değil: demo verisi ve testler hakkı doğrudan yazar.
- **İsteğe bağlı bağımlılık uyarısı dilim 3'e kaydı.** Uyarı yanıt sözleşmelerini ve platform ekranını birlikte değiştirir; kural
  (`ModuleDependencyRules.Missing`) isteğe bağlı eksikleri şimdiden döndürür.

### 4. Değişmeyenler

- **Çalışma zamanı:** ADR-0044'teki `Unavailable` kalıbı aynen kalır. Beyan çalışma zamanında modülü kapatmaz, istek reddetmez.
- **Lisans süresi (ADR-0028):** sağlayan modülün ek süreye ya da salt okunura düşmesi bağımlı modülü etkilemez; okuma sürer, sağlayana
  yazan çağrı bugünkü gibi sonuç döner. Hakkın süresi bitip modül kapanınca bağımlı modül açık kalır ve `Unavailable` görür; bu durum
  platform panosunda "bağımlılığı eksik firmalar" olarak listelenir, kendiliğinden kapatma yapılmaz.
- **Mevcut firmalar:** kural geriye dönük uygulanmaz, migration yoktur. Kuralı ihlal eden mevcut haklar raporlanır; bir sonraki hak
  değişikliğinde denetim devreye girer.
- **Otomasyon:** `AutomationActionDefinition.RequiredModules` aksiyon düzeyinde kalır; modül düzeyindeki beyanın yerine geçmez.

### 5. İlk beyanlar

| Modül | Bağımlılık | Tür | Gerekçe |
|---|---|---|---|
| Sipariş | CRM | İsteğe bağlı | Tekliften sipariş, takip aktivitesi; sipariş elle de açılır |
| Sözleşme | CRM | İsteğe bağlı | Tekliften sözleşme taslağı, takip aktivitesi |
| Lojistik | Filo | İsteğe bağlı | Seferde araç ve dorse seçimi |
| Pazarlama | CRM | Zorunlu | Alıcı ve segment alanlarını yalnızca CRM sağlar |

## Seçenekler

1. **Beyan yok, yalnızca belge.** Bugünkü durum; platform yöneticisi çalışmayan birleşim açabilir. Reddedildi.
2. **Beyanı proje başvurusundan türetmek.** Yön yanlış çıkar (genişleme noktaları) ve zorunlu/isteğe bağlı ayrımı bilinmez. Reddedildi;
   bunun yerine başvuru ile beyanın eşleşmesi testle denetlenir (karar 2).
3. **Zorunlu bağımlılığı kendiliğinden açmak.** Lisans kaydını (kaynak, bitiş, gerekçe) kimin adına yazacağı belirsiz. Reddedildi.
4. **Bu ADR:** kodda beyan, açılışta doğrulama, hak yazılırken denetim.

## Açık soruların kararı (16 Kasım 2026)

1. **Pazarlama → CRM zorunludur.** Bugün başka alıcı kaynağı yok; ikinci bir alıcı kaynağı gelirse beyan isteğe bağlıya çevrilir.
2. **Bağımlısı olan modülü kapatma reddedilir**, zincirleme kapama yapılmaz. Yanıt bağımlı modülleri listeler; platform yöneticisi önce
   onları kapatır.
3. **Firma yöneticisi bağımlılığı yalnızca bilgi olarak görür** (dilim 3, firma Modüller ekranı: "CRM açık olmadığı için tekliften sipariş
   kullanılamaz"). Hak değişikliği yalnızca platformdadır.
4. **Mevcut ihlaller yalnızca raporlanır**; kendiliğinden düzeltme ya da görev açma yoktur.

## Dilimler

| Dilim | İçerik | Test |
|---|---|---|
| 1 | `IModule.Dependencies`, katalog doğrulaması, ilk beyanlar, başvuru-beyan eşleşme testi | Birim (doğrulama), mimari |
| 2 | `ModuleDependencyRules` ve altı hak yolunda zorunlu bağımlılık denetimi | Mutlu yol, ret, tenant izolasyonu, yetki (platform rolü) |
| 3 | İsteğe bağlı bağımlılık uyarısı (yanıt + ekran); platform arayüzü: modül satırında bağımlılık bilgisi, ret iletisi, "bağımlılığı eksik firmalar" raporu; firma Modüller ekranında bilgi | Entegrasyon + arayüz görsel kontrolü |

Dilim 3 ikiye bölündü:

- **3a (platform, teslim edildi).** `GET /api/platform/modules` ve `/catalog` modülün bağımlılıklarını döndürür; firma yanıtı
  `missingDependencies` taşır (zorunlu ve isteğe bağlı eksikler, gerekçesiyle); `GET /api/platform/dependency-gaps` zorunlu bağımlılığı
  eksik, erişime açık firmaları listeler. Ekran: firma detayında modülün altında bağımlılık satırı ve eksikler için uyarı/bilgi kutusu,
  katalogda bağımlılık satırı, platform ana sayfasında "Bağımlılığı eksik firmalar".
- **3b (firma tarafı, teslim edildi).** Firma tarafında modülleri listeleyen ayrı bir ekran olmadığı için bilgi Yönetim ana sayfasına
  ("Modül bağımlılıkları" bölümü, yalnızca eksik varsa) kondu. Veri `GET /api/tenant/access` yanıtındaki `missingDependencies` ile gelir
  (lisans uyarısıyla aynı kalıp); yalnızca firma yöneticisine dolar. Her sayfada görünen bant yapılmadı: isteğe bağlı eksik kalıcı bir
  durumdur, uyarı değil bilgidir.

Karar 3'teki tablodan sapma: isteğe bağlı eksik için uyarı, hak yazan altı işlemin yanıtında değil firma yanıtında
(`missingDependencies`) döner. Modül açma/kapama ve plan atama gövdesiz 204 döndürür; ekran işlemden sonra firmayı zaten yeniden okur.
Böylece yanıt sözleşmeleri değişmez ve uyarı işlemden sonra da görünür kalır. Rapor yalnızca zorunlu eksikleri listeler: isteğe bağlı
eksik (ör. CRM'siz Sipariş) geçerli bir ticari durumdur. Gerekçe metni (`Reason`) kodda Türkçe yazılıdır ve çevrilmeden gösterilir.

## Sonuçlar

- Yeni modül eklerken `Dependencies` de yazılır; başka modülün sözleşmesine beyansız başvuru mimari testi kırar.
- Platform yöneticisi çalışmayan modül birleşimi açamaz ve bir modülü kapatırken etkisini görür.
- Plan ve sektör profili tanımları kapalı küme olmak zorunda kalır; mevcut plan ve profiller dilim 2'de gözden geçirilir.
- Çalışma zamanı ve URL'ler değişmez; şema değişikliği yoktur.
