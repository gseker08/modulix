# ADR-0055: Çalışma alanı rengi ve rol bazlı menü düzeni

- **Durum:** Dilim 0 (tasarım sisteminde firmanın değiştirebileceği tokenlar, PR #509), dilim 1 (firma vurgu rengi ve sunucuda kontrast
  doğrulaması, PR #513), dilim 2a (rol menü düzeni, sunucu, PR #525) ve dilim 2b (menüye uygulama ve rol ekranında düzen düzenleme, PR #528;
  kayıtta ekranda olmayan satırların korunması, PR #533) 22 Kasım 2026'da teslim edildi; sırası verilmeyen öğenin açık sıralıların arkasına alınması 23 Kasım 2026'da eklendi; bölüm sırası (dilim 3) 22 Kasım 2026'da teslim edildi. Bu ADR kod teslim edildikten sonra yazıldı:
  kararlar o PR'lardaki gerçek davranışı kayda geçirir. Dilim 4 (uygulama sırası) kodla birlikte yazıldı: uygulamanın yalnızca kendi uygulama paneli
  grubu içinde yer değiştirmesi bu dilimin kararıdır.
- **Kaynak:** [CRM eksik süreç epic listesi](../crm-eksik-surecler-epicleri.md) E8. Tasarım sistemi tarafı:
  [Tema tokenları](../design-system/theme-tokens.md) "Firmanın değiştirebileceği tokenlar".

## Bağlam

Rakip CRM'ler "size özel, logolu ve renkli çalışma alanı" sunuyor; Modulix'te logo vardı, firma rengi ve menü düzeni yoktu. İki ilke sınırlıyor:

- Arayüz tasarım kuralları renk, köşe ve gölgenin yalnızca `--mx-*` tokenlarından gelmesini ister; firma bileşene ya da ekrana özel renk veremez.
- Yetki sunucuda ve sorgu seviyesinde uygulanır; menüde bir öğeyi gizlemek yetki değildir.

## Karar

### Firma yalnızca kapalı bir token listesini değiştirir

Firmanın değiştirebildiği tek şey **vurgu rengidir** (`--mx-brand-primary`); açık ve koyu tema için ayrı değer girilir. Yüzey, metin, durum, ton,
tipografi, boşluk, köşe, gölge, CTA degradesi ve marka turkuazı bütün firmalarda aynıdır. Liste büyütülecekse önce tasarım sistemindeki tabloya
kontrast kuralıyla birlikte yazılır (`theme-tokens.md`).

- **Depolama:** mevcut firma ayarları (`core.appearance.accent-light`, `core.appearance.accent-dark`; `#RRGGBB`, en çok 7 karakter, boş = Modulix
  rengi). Migration yoktur. Okuma `GET /api/settings`, yazma `PUT /api/settings` (`core.settings.manage`, kayıt boru hattı: denetim kaydı ve sürüm
  denetimi). Ayrı bir marka ucu yoktur; ayar kataloğu tek yoldur.
- **Doğrulama sunucuda yapılır** (`Modulix.Core/Settings/AccentColor.cs`, `SettingRules` çağırır). Biçim `^#[0-9A-Fa-f]{6}$` (ad, `rgb()`, `var()`,
  alfa kabul edilmez; bu yüzden değer CSS'e yazılırken enjeksiyon olmaz). Kontrast WCAG göreli parlaklığıyla hesaplanır, eşik 4.5:1:
  açık temada vurgu rengi beyaz yüzeyle ve beyaz marka üstü metinle, koyu temada üç koyu yüzeyle (`#121D33`, `#0B1322`, `#192842`) ve
  koyu marka üstü metinle (`#07111F`). Geçmeyen renk 422 ile reddedilir; ileti hangi temada ve kaç:1 olduğunu söyler. Yüzey değerleri `tokens.css`'tekilerdir:
  orada değişirse `AccentColor` da değişir, birim test aynı değerleri sabitler.
- **Uygulama:** Web oturum yüklenince iki ayarı okur (`SessionState`) ve `modulixShell.setAccent` ile tek bir `<style id="mx-accent-style">` yazar;
  yalnızca `--mx-brand-primary`, `-rgb` ve `-hover` tokenları açık/koyu temaya göre değişir, bileşen CSS'i değişmez. Ayar yoksa hiçbir şey yazılmaz.
- **Renk seçici ve önizleme:** Yönetim > Ayarlar > Görünüm'de iki ayar `AccentColorField` ile girilir (renk seçici, `#RRGGBB` kutusu, "Varsayılana dön", seçilen temanın yüzeyinde önizleme). Kaydetmeden önce kontrast `POST /api/settings/accent/preview` (`{ color, dark }` → `{ valid, contrast, minContrast, message }`, `core.settings.manage`) ile sunucudaki aynı `AccentColor` kuralından sorulur; uç hiçbir şey yazmaz (yolu `/preview` ile biter) ve ikinci bir kural tanımlamaz. Kaydetmeyi yine sunucu doğrular (422), önizleme yalnızca erken geri bildirimdir.
- **Hover sunucuda ayrıca doğrulanmaz.** Hover tonu istemcide (`shell.js`) türetilir: açık temada rengin %12'si siyaha, koyu temada %12'si beyaza
  doğru çekilir. Açık temada koyulaşan renk beyazla, koyu temada açılan renk koyu yüzey ve metinle daha çok kontrast verir; yani temel renk kuralı
  geçtiyse türetilen ton da geçer ve ayrı bir sunucu kontrolü hiç devreye girmez. Bu, formülün yönüne dayanır: türetme yön değiştirirse
  (açık temada beyaza, koyu temada siyaha doğru) ya da yüzeyler değişirse yeniden değerlendirilir. Doğrulama için temel kuralı geçen yaklaşık 1,67
  milyon rastgele renk (her temada 2 milyon denemeden) türetilen tonla denendi; hiçbirinde kural bozulmadı.

### Rol bazlı menü düzeni: sıra ve gizleme, öğe düzeyinde

Menü düzeni **rol başınadır** ve yalnızca görünümdür. Kapsam öğe düzeyindedir: rol bir menü satırını (uygulama kataloğundaki öğe) gizleyebilir
ve satırın bölüm içindeki sırasını değiştirebilir; ayrıca uygulamanın menüsündeki **bölümlerin** sırasını (dilim 3) ve uygulamanın uygulama
panelindeki **sırasını** (dilim 4) değiştirebilir. Uygulamanın uygulama paneli grubu ve grupların sırası sabittir.

- **Veri (dilim 2a, migration `20261122161620_CoreRoleMenuItems`):** `core.RoleMenuItems` (RoleId, ItemKey, SortOrder, Hidden). Tenant verisidir
  (`ITenantEntity`, `ICompanyShared`): `TenantId` taşır, RLS ile korunur, role bileşik yabancı anahtarla bağlıdır, rol silinince satırlar silinir.
  `ItemKey` arayüzdeki `AppCatalog` kimliğidir (`crm.companies`); sunucu yalnızca biçimi doğrular (küçük harf, rakam, nokta, tire; en çok 100 karakter),
  katalogda olmayan satırı arayüz yok sayar. `SortOrder` 0–10000 arasıdır; `null` katalog sırası demektir.
- **Uç noktalar:** `GET/PUT /api/roles/{roleId}/menu`, izin `core.roles.manage` (yeni izin anahtarı yok). PUT bütün düzeni değiştirir, boş liste
  düzeni kaldırır. Geçersiz ya da tekrarlı anahtar ve aralık dışı sıra doğrulama hatasıdır. Değişiklik rolün denetim kaydına `menu` alanı olarak yazılır.
- **Yönetici (sistem rolü) düzeni tutmaz:** sistem rolüne yazmak 409 döner ve yönetici menüyü her zaman tam görür; böylece düzen ekranına erişim
  gizlenerek kaybedilemez.
- **Kullanıcının menüsü:** `GET /api/tenant/access` yanıtı `RoleIds` (kullanıcının rolleri) ve `MenuLayout` (yalnızca kullanıcının rollerinin
  satırları; yöneticide boş) taşır. Birleştirme istemcide `MenuLayoutRules` ile yapılır:
  1. Yalnızca düzeni tanımlı (en az bir satırı olan) roller sayılır; hiçbiri yoksa katalog düzeni aynen kalır.
  2. Bir öğe, düzeni tanımlı rollerinden **herhangi birinde** görünüyorsa görünür. Rolde öğenin satırı yoksa öğe o rolde görünür sayılır; yalnızca
     açıkça gizlenmişse görünmez.
  3. Sıra, öğeyi gösteren ve ona sıra veren rollerin en küçüğüdür. Bölümde en az bir öğeye açık sıra verilmişse, sıra verilmeyen öğeler
     (hiçbir rolün satırında sırası yok) açık sıralı öğelerin arkasına katalog sırasıyla dizilir; katalog numaraları ile rolün yazdığı
     numaralar (10, 20, …) aynı ölçekte olmadığından karıştırılmaz. Bölümde hiçbir öğeye sıra verilmemişse katalog sırası kalır. Sıra bölüm
     içinde uygulanır, öğe bölümünü değiştirmez.
  4. **Bölüm sırası (dilim 3)** aynı tabloda `section.<uygulama>.<bölüm>` anahtarlı satırla saklanır (`MenuLayoutRules.SectionKey`, ör.
     `section.crm.crm.customers`); `SortOrder` bölümün uygulamanın menüsündeki sırasıdır, `Hidden` kullanılmaz. Sunucu yeni bir kural,
     tablo ya da migration eklemez: anahtar öğe anahtarıyla aynı biçim doğrulamasından (küçük harf, rakam, nokta, tire) geçer. Kural öğeyle
     aynıdır: sıra veren rollerin en küçüğü; sırası verilmeyen bölüm açık sıralıların arkasına katalog sırasıyla gelir (`MenuLayoutRules.OrderSections`). Yalnızca başlıklı ve
     alt sabitli (`Footer`) olmayan bölümler yer değiştirir; alt sabitli (CRM Ayarları) ve başlıksız ("Diğer") bölümler yerinde kalır.
     Başka uygulamanın ya da katalogda olmayan bölümün satırı etkisizdir; bölüm satırı öğe görünürlüğünü etkilemez ve bölümü gizlemez
     (bölüm düzeyinde gizleme yoktur, bir bölüm yalnızca bütün öğeleri gizlenince kaybolur).
  5. **Uygulama sırası (dilim 4)** aynı tabloda `app.<uygulama>` anahtarlı satırla saklanır (`MenuLayoutRules.AppKey`, ör. `app.crm`); `SortOrder`
     uygulamanın sırasıdır, `Hidden` kullanılmaz. Şema, migration ve sunucu kuralı değişmez (anahtar aynı biçim doğrulamasından geçer). Sıra
     uygulamanın **uygulama paneli grubu içinde** uygulanır (`AppNavigation.Roots`, `MenuLayoutRules.OrderApps`): uygulama başka gruba geçmez,
     grupların sırası katalogdadır. Kural öğeyle aynıdır: sıra veren rollerin en küçüğü; bir grupta en az bir uygulamaya açık sıra verilmişse
     sırası verilmeyen uygulamalar açık sıralıların arkasına katalog sırasıyla dizilir. Öğe ya da bölüm satırı, başka grubun satırı ve bilinmeyen
     anahtar etkisizdir. Uygulama gizlenemez: kaybolma yalnızca modül, izin ve lisans süzgecinden olur.
- **Gizlemek yetki değildir.** Düzen, modül, izin ve lisans süzgecinin **üstüne** biner; hiçbir zaman izinsiz bir öğeyi görünür yapmaz. Gizli
  öğenin adresi doğrudan açılırsa sayfa yine açılır (erişimi izin ve sunucu belirler). Bir uygulamanın bütün satırları gizlense de uygulama
  uygulama panelinden kaybolmaz.
- **Düzenleme ekranı (dilim 2b):** Yönetim > Roller > rol detayında "Menü düzeni" (`RoleMenuLayout`). Her uygulamanın satırları bölümleriyle
  listelenir; satır başına görünür/gizli anahtarı ve yukarı/aşağı düğmeleri vardır. Ekrandaki satırlar kullanıcının izninden bağımsızdır, firmada
  modülü açık uygulamalardan gelir (platforma özel olanlar hariç). Başlıklı ve taşınabilir bölümlerin başlığında yukarı/aşağı düğmeleri vardır (dilim 3); uygulama başlığındaki düğmeler uygulamayı aynı uygulama
  paneli grubundaki komşusuyla yer değiştirir (dilim 4). Sistem rolünde bölüm görünmez.
- **Kayıt biçimi:** yalnızca varsayılandan ayrılan bölümler yazılır. Sırası değişen bölümde bütün satırlar açık sıra numarasıyla (10, 20, …)
  yazılır; sırası değişmeyen bölümde sıra `null` kalır ve yalnızca gizlenenler yazılır. Bölümlerin sırası değişen uygulamada bütün taşınabilir
  bölümler `section.…` satırıyla 10, 20, … yazılır; bölümler varsayılan sırasındaysa bölüm satırı yazılmaz. Uygulama sırası değişince ekrandaki bütün uygulamalar `app.…` satırıyla 10, 20, … yazılır. PUT bütün düzeni değiştirdiği için ekranda listelenmeyen
  (örneğin modülü şu an kapalı) öğelerin kayıtlı satırları olduğu gibi geri gönderilir (`RoleMenuSaveRules`, PR #533); aksi halde modül sonradan
  açılınca rolün düzeni kaybolurdu. "Varsayılana dön" ekranı sıfırlar, kaydedilince düzen kalkar.

## Dilimler

0. **Tasarım sistemi (22 Kasım 2026, PR #509):** "Firmanın değiştirebileceği tokenlar" bölümü, izinli liste ve kontrast kuralı.
1. **Vurgu rengi (22 Kasım 2026, PR #513):** iki firma ayarı, `AccentColor` kuralı, oturumda uygulama; migration yok.
   - **Renk seçici (22 Kasım 2026):** ayar ekranında seçici ve `POST /api/settings/accent/preview` önizleme ucu (`a8ecdd1a`).
2. **Rol menüsü:**
   - **2a (22 Kasım 2026, PR #525):** tablo, servis, uç noktalar, erişim yanıtında `RoleIds` ve `MenuLayout`.
   - **2b (22 Kasım 2026, PR #528, #533):** menüye uygulama (`MenuLayoutRules`, `AppNavigation`), rol ekranında düzenleyici, kayıtta olmayan satırların korunması.
   - **Sıra kuralı düzeltmesi (23 Kasım 2026, PR #552):** sırası verilmeyen öğe açık sıralıların arkasına alındı (kural 3). Aynı kural bölümlere ve uygulamalara da uygulandı (kural 4 ve 5).
3. **Bölüm sırası (22 Kasım 2026, `d76bbdbc`):** bölüm başlığında yukarı/aşağı, `section.<uygulama>.<bölüm>` satırı, `MenuLayoutRules.OrderSections`; şema ve migration değişmedi.
4. **Uygulama sırası (23 Kasım 2026, PR #561):** uygulama başlığında yukarı/aşağı, `app.<uygulama>` satırı, `MenuLayoutRules.OrderApps` ve `AppNavigation.Roots`; uygulama
   kendi uygulama paneli grubu içinde yer değiştirir; şema ve migration değişmedi.

## Yapılmadı / sınırlar

- **Uygulama paneli grupları** düzenlenemez (grup sırası ve bir uygulamanın grubu katalogdadır); uygulama yalnızca kendi grubu içinde yer değiştirir.
  Uygulama gizlenemez. Hiçbir rolün sıra satırı olmayan uygulama (ör. katalog sonradan yeni uygulama eklerse) grubun sonuna gider; öğelerde olduğu
  gibi sırasız öğe arkaya alınır; aynı kural bölümlerde de geçerlidir (kural 4).
- **Bölüm düzeyinde gizleme:** bölüm gizlenemez; bir bölüm yalnızca bütün öğeleri gizlenince kaybolur.
- **CTA degradesi ve marka turkuazı** sabit onaltılık değerle yazıldığı için vurgu rengine bağlı değildir. Bağlanması önce `tokens.css`'te türetmeye
  çevrilmeyi ve ayrı bir kontrast kuralını gerektirir.
- **Görsel doğrulama:** vurgu rengi ve rol menüsü ekranlarının açık ve koyu temada, telefon genişliğinde ekran görüntüsü kontrolü yapılmadı
  (CLAUDE.md arayüz kuralı). Düzenleyicideki "kaydederken ekranda olmayan satırları ekle" adımı Razor bileşeninin içindedir ve bileşen düzeyinde
  testi yoktur; mantığın kendisi `RoleMenuSaveRules` birim testleriyle sınanır.
- **Entegrasyon testleri:** uç noktaların yetki, tenant izolasyonu, sistem rolü ve doğrulama senaryoları `RoleMenuLayoutTests` ve
  `SettingsTests` içindedir; yerel ortamda koşmadı, CI'da koşar.
