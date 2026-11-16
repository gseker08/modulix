# ADR-0051: Modül bağımlılık beyanı

- **Durum:** Öneri (16 Kasım 2026). Kod yazılmadı; aşağıdaki açık sorular karara bağlanınca dilim 1 başlar.
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

Açılışta `ModuleCatalog` doğrular: bilinmeyen modül anahtarı, modülün kendisi, `core` (her zaman açıktır, beyan edilmez), aynı modüle iki
beyan ve `Required` bağımlılıklarda döngü açılışı durdurur.

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

### 4. Değişmeyenler

- **Çalışma zamanı:** ADR-0044'teki `Unavailable` kalıbı aynen kalır. Beyan çalışma zamanında modülü kapatmaz, istek reddetmez.
- **Lisans süresi (ADR-0028):** sağlayan modülün ek süreye ya da salt okunura düşmesi bağımlı modülü etkilemez; okuma sürer, sağlayana
  yazan çağrı bugünkü gibi sonuç döner. Hakkın süresi bitip modül kapanınca bağımlı modül açık kalır ve `Unavailable` görür; bu durum
  platform panosunda "bağımlılığı eksik firmalar" olarak listelenir, kendiliğinden kapatma yapılmaz.
- **Mevcut firmalar:** kural geriye dönük uygulanmaz, migration yoktur. Kuralı ihlal eden mevcut haklar raporlanır; bir sonraki hak
  değişikliğinde denetim devreye girer.
- **Otomasyon:** `AutomationActionDefinition.RequiredModules` aksiyon düzeyinde kalır; modül düzeyindeki beyanın yerine geçmez.

### 5. İlk beyanlar (öneri)

| Modül | Bağımlılık | Tür | Gerekçe |
|---|---|---|---|
| Sipariş | CRM | İsteğe bağlı | Tekliften sipariş, takip aktivitesi; sipariş elle de açılır |
| Sözleşme | CRM | İsteğe bağlı | Tekliften sözleşme taslağı, takip aktivitesi |
| Lojistik | Filo | İsteğe bağlı | Seferde araç ve dorse seçimi |
| Pazarlama | CRM | Zorunlu (açık soru 1) | Alıcı ve segment alanlarını yalnızca CRM sağlar |

## Seçenekler

1. **Beyan yok, yalnızca belge.** Bugünkü durum; platform yöneticisi çalışmayan birleşim açabilir. Reddedildi.
2. **Beyanı proje başvurusundan türetmek.** Yön yanlış çıkar (genişleme noktaları) ve zorunlu/isteğe bağlı ayrımı bilinmez. Reddedildi;
   bunun yerine başvuru ile beyanın eşleşmesi testle denetlenir (karar 2).
3. **Zorunlu bağımlılığı kendiliğinden açmak.** Lisans kaydını (kaynak, bitiş, gerekçe) kimin adına yazacağı belirsiz. Reddedildi.
4. **Bu ADR:** kodda beyan, açılışta doğrulama, hak yazılırken denetim.

## Açık sorular

1. **Pazarlama → CRM zorunlu mu?** Öneri: zorunlu. Alternatif: isteğe bağlı bırakıp Pazarlama'yı CRM'siz "alıcısız" açılabilir tutmak;
   bugün başka alıcı kaynağı olmadığı için satılabilir bir birleşim değil.
2. **Kapama reddi mi, zincirleme kapama mı?** Öneri: ret (bağımlıları listeler); platform yöneticisi önce bağımlı modülü kapatır.
3. **Firma yöneticisi görsün mü?** Öneri: dilim 3'te firma Modüller ekranında yalnızca bilgi olarak ("CRM açık olmadığı için tekliften
   sipariş kullanılamaz"). Hak değişikliği yine yalnızca platformdadır.
4. **Mevcut ihlaller:** yalnızca rapor (öneri) mi, yoksa platform yöneticisine düzeltme görevi mi?

## Dilimler

| Dilim | İçerik | Test |
|---|---|---|
| 1 | `IModule.Dependencies`, katalog doğrulaması, ilk beyanlar, başvuru-beyan eşleşme testi | Birim (doğrulama), mimari |
| 2 | `ModuleDependencyRules` ve altı hak yolunda denetim; platform yanıtlarında uyarı | Mutlu yol, ret, tenant izolasyonu, yetki (platform rolü) |
| 3 | Platform arayüzü: modül satırında bağımlılık bilgisi, ret iletisi, "bağımlılığı eksik firmalar" raporu; firma Modüller ekranında bilgi | Entegrasyon + arayüz görsel kontrolü |

## Sonuçlar

- Yeni modül eklerken `Dependencies` de yazılır; başka modülün sözleşmesine beyansız başvuru mimari testi kırar.
- Platform yöneticisi çalışmayan modül birleşimi açamaz ve bir modülü kapatırken etkisini görür.
- Plan ve sektör profili tanımları kapalı küme olmak zorunda kalır; mevcut plan ve profiller dilim 2'de gözden geçirilir.
- Çalışma zamanı ve URL'ler değişmez; şema değişikliği yoktur.
