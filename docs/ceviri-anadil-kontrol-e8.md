# Çeviri anadil kontrolü: E8 (çalışma alanı rengi ve rol menüsü)

Bu belge, E8 (R4) dilimlerinde eklenen arayüz metinlerinin **İngilizce (en)** ve **Azerice (az)** karşılıklarını anadili konuşan bir gözden geçiren için toplar. Türkçe (tr) metinler kaynaktır; en/az metinleri taslaktır ve anadil kontrolünden geçmemiştir (ADR-0034, "taslak" kuralı). Toplam **35 anahtar**.

## Nasıl gözden geçirilir

1. Her satırda Türkçe kaynağı, "Nerede görünür" sütunundaki bağlamı ve en/az metnini okuyun. Metin doğal ve kısa mı, terim tutarlı mı bakın.
2. Düzeltme gerekirse son sütuna önerinizi yazın (ya da doğrudan `src/Modulix.Core/Localization/Texts/en.json` / `az.json` dosyasındaki aynı anahtarı düzeltin).
3. **Değişmeyecekler:** `{0}`, `{1}` yer tutucuları (aynı sayıda ve anlamda kalır), `#RRGGBB`, örnek renk kodu `#087DB8`, "Modulix" adı. Yer tutucu sayısı eşleşmezse `LocalizationRulesTests` kırılır.
4. Anahtar adlarını değiştirmeyin; yalnızca metin değişir. Düzeltmeler tek bir küçük PR ile gelir; `dotnet test tests/Modulix.Core.Tests` ve mimari testler geçmelidir.

## Terim tutarlılığı için sorular (en/az)

- "Vurgu rengi": en "Accent color" / az "Vurğu rəngi" doğal mı? Başka bir terim (ör. "Brand color / Marka rəngi") daha mı uygun?
- "Menü düzeni" (en "Menu layout", az "Menyu düzəni"), "Menü satırı", "Bölüm" (menüdeki grup başlığı) ve "Uygulama" (menüdeki ana modül): üç dilde tutarlı mı?
- "Görünür" (en "Visible", az "Görünür") ve "Varsayılana dön" (en "Reset to default", az "Susmaya görə vəziyyətə qayıt") kalıpları, uygulamanın diğer ekranlarındaki karşılıklarıyla aynı mı? az "Susmaya görə" karşılığı doğal mı, "Standarta qayıt" gibi daha kısa bir kalıp mı daha uygun?
- Kontrast iletilerindeki "okunmuyor / not readable enough" ifadesi hata tonunda doğru mu?
- az için: Latin alfabesi ve Azərbaycan Türkcəsi yazım kuralları (ə, ı, ş, ç, ğ, ö, ü) doğru mu? Erişilebilirlik etiketlerinde ("... bölməsini yuxarı apar") fiil biçimi doğal mı?

## Metinler

| Anahtar | Nerede görünür | tr (kaynak) | en | az | Gözden geçirenin notu / öneri |
|---|---|---|---|---|---|
| `admin.roleMenu.title` | Rol detayında "Menü düzeni" sekmesinin ve bölümünün başlığı | Menü düzeni | Menu layout | Menyu düzəni | |
| `admin.roleMenu.help` | Bölümün açıklama paragrafı | Bu roldeki kullanıcıların sol menüsünde hangi satırların görüneceğini ve sırasını belirleyin. Menüde gizlemek yetki değildir: ekranlar izinle korunmaya devam eder. Birden fazla rolü olan kullanıcıda bir satır, düzeni tanımlı rollerinden herhangi birinde görünüyorsa görünür. | Choose which rows appear in the side menu of users with this role, and in what order. Hiding a row is not a permission: screens stay protected by permissions. For a user with several roles, a row is shown if it is visible in any of their roles that define a layout. | Bu roldakı istifadəçilərin yan menyusunda hansı sətirlərin görünəcəyini və sırasını müəyyən edin. Menyuda gizlətmək icazə deyil: ekranlar icazələrlə qorunmağa davam edir. Bir neçə rolu olan istifadəçidə sətir, düzəni təyin olunmuş rollarından hər hansı birində görünürsə görünür. | |
| `admin.roleMenu.empty` | Düzenlenecek menü yoksa boş durum başlığı | Düzenlenecek menü satırı yok. | There are no menu rows to arrange. | Düzənlənəcək menyu sətri yoxdur. | |
| `admin.roleMenu.ungrouped` | Başlığı olmayan menü satırlarının bölüm adı | Diğer | Other | Digər | |
| `admin.roleMenu.col.item` | Tablo sütun başlığı | Menü satırı | Menu row | Menyu sətri | |
| `admin.roleMenu.col.visible` | Tablo sütun başlığı (görünür/gizli anahtarı) | Görünür | Visible | Görünür | |
| `admin.roleMenu.col.order` | Sütun adı; ekranda görünmez, ekran okuyucu içindir | Sıra | Order | Sıra | |
| `admin.roleMenu.up` | Düğme erişilebilirlik etiketi; {0} = menü satırı adı | {0} satırını yukarı taşı | Move {0} up | {0} sətrini yuxarı apar | |
| `admin.roleMenu.down` | Düğme erişilebilirlik etiketi; {0} = menü satırı adı | {0} satırını aşağı taşı | Move {0} down | {0} sətrini aşağı apar | |
| `admin.roleMenu.sectionUp` | Düğme erişilebilirlik etiketi; {0} = bölüm adı | {0} bölümünü yukarı taşı | Move section {0} up | {0} bölməsini yuxarı apar | |
| `admin.roleMenu.sectionDown` | Düğme erişilebilirlik etiketi; {0} = bölüm adı | {0} bölümünü aşağı taşı | Move section {0} down | {0} bölməsini aşağı apar | |
| `admin.roleMenu.appUp` | Düğme erişilebilirlik etiketi; {0} = uygulama adı | {0} uygulamasını yukarı taşı | Move app {0} up | {0} tətbiqini yuxarı apar | |
| `admin.roleMenu.appDown` | Düğme erişilebilirlik etiketi; {0} = uygulama adı | {0} uygulamasını aşağı taşı | Move app {0} down | {0} tətbiqini aşağı apar | |
| `admin.roleMenu.reset` | İkincil düğme | Varsayılana dön | Reset to default | Susmaya görə vəziyyətə qayıt | |
| `admin.roleMenu.saved` | Kayıt sonrası bildirim (toast) | Menü düzeni kaydedildi. | Menu layout saved. | Menyu düzəni saxlanıldı. | |
| `admin.settings.accent.picker` | Renk seçici girdisinin erişilebilirlik etiketi | Renk seçici | Color picker | Rəng seçici | |
| `admin.settings.accent.reset` | Düğme: rengi boşaltıp varsayılana döner | Varsayılana dön | Reset to default | Susmaya görə vəziyyətə qayıt | |
| `admin.settings.accent.checking` | Kontrast hesaplanırken gösterilen durum metni | Kontrast kontrol ediliyor… | Checking contrast… | Kontrast yoxlanılır… | |
| `admin.settings.accent.ok` | Renk uygunsa durum metni; {0} = ölçülen kontrast, {1} = gereken en düşük kontrast | Okunabilirlik uygun: kontrast {0}:1 (en az {1}:1 gerekir). | Readable: contrast {0}:1 (at least {1}:1 required). | Oxunaqlıdır: kontrast {0}:1 (ən azı {1}:1 tələb olunur). | |
| `admin.settings.accent.lowContrastLight` | Hata: açık temada yetersiz kontrast; {0} ölçülen, {1} gereken | Bu renk açık temada yeterince okunmuyor (kontrast {0}:1, en az {1}:1 gerekir). | This color is not readable enough in the light theme (contrast {0}:1, at least {1}:1 required). | Bu rəng açıq temada kifayət qədər oxunmur (kontrast {0}:1, ən azı {1}:1 tələb olunur). | |
| `admin.settings.accent.lowContrastDark` | Hata: koyu temada yetersiz kontrast; {0} ölçülen, {1} gereken | Bu renk koyu temada yeterince okunmuyor (kontrast {0}:1, en az {1}:1 gerekir). | This color is not readable enough in the dark theme (contrast {0}:1, at least {1}:1 required). | Bu rəng tünd temada kifayət qədər oxunmur (kontrast {0}:1, ən azı {1}:1 tələb olunur). | |
| `admin.settings.accent.badFormat` | Hata: biçim yanlış; "#RRGGBB" ve örnek renk kodu olduğu gibi kalır | Renk #RRGGBB biçiminde olmalıdır (ör. #087DB8). | The color must be in #RRGGBB format (e.g. #087DB8). | Rəng #RRGGBB formatında olmalıdır (məs. #087DB8). | |
| `admin.settings.accent.default` | Renk boşken durum metni | Ayarlanmadı: Modulix rengi kullanılır. | Not set: the Modulix color is used. | Təyin edilməyib: Modulix rəngi istifadə olunur. | |
| `admin.settings.accent.preview` | Önizleme kutusunun erişilebilirlik etiketi | Renk önizlemesi | Color preview | Rəng önbaxışı | |
| `admin.settings.accent.sampleLink` | Önizlemedeki örnek bağlantı metni | Örnek bağlantı | Sample link | Nümunə keçid | |
| `admin.settings.accent.sampleButton` | Önizlemedeki örnek düğme metni | Örnek düğme | Sample button | Nümunə düymə | |
| `admin.settings.accent.lightTheme` | Önizleme kutusunda tema adı | Açık tema | Light theme | Açıq tema | |
| `admin.settings.accent.darkTheme` | Önizleme kutusunda tema adı | Koyu tema | Dark theme | Tünd tema | |
| `meta.field.core.role.menu` | Denetim kaydında rolün değişen alanı olarak görünen ad | Menü düzeni | Menu layout | Menyu düzəni | |
| `meta.setting.core.appearance.accent-dark` | Kuruluş ayarları > Görünüm: koyu tema vurgu rengi ayarının adı | Vurgu rengi (koyu tema) | Accent color (dark theme) | Vurğu rəngi (tünd tema) | |
| `meta.setting.core.appearance.accent-dark.group` | Ayar grubunun adı | Görünüm | Appearance | Görünüm | |
| `meta.setting.core.appearance.accent-dark.description` | Ayarın açıklaması | Koyu temadaki vurgu rengi; #RRGGBB. Boşsa Modulix rengi kullanılır. Koyu zeminde en az 4.5:1 kontrast gerekir. | Accent color in the dark theme; #RRGGBB. If empty, the Modulix color is used. Needs at least 4.5:1 contrast on a dark background. | Tünd temada vurğu rəngi; #RRGGBB. Boşdursa Modulix rəngi istifadə olunur. Tünd fonda ən azı 4.5:1 kontrast tələb olunur. | |
| `meta.setting.core.appearance.accent-light` | Kuruluş ayarları > Görünüm: açık tema vurgu rengi ayarının adı | Vurgu rengi (açık tema) | Accent color (light theme) | Vurğu rəngi (açıq tema) | |
| `meta.setting.core.appearance.accent-light.group` | Ayar grubunun adı | Görünüm | Appearance | Görünüm | |
| `meta.setting.core.appearance.accent-light.description` | Ayarın açıklaması | Bağlantı, odak ve etkin sekme rengi; #RRGGBB. Boşsa Modulix rengi kullanılır. Okunabilirlik için açık zeminde en az 4.5:1 kontrast gerekir. | Color of links, focus and the active tab; #RRGGBB. If empty, the Modulix color is used. Needs at least 4.5:1 contrast on a light background. | Keçid, fokus və aktiv sekmə rəngi; #RRGGBB. Boşdursa Modulix rəngi istifadə olunur. Açıq fonda ən azı 4.5:1 kontrast tələb olunur. | |

## Sınırlar

- Bu liste yalnızca E8 ile eklenen anahtarları kapsar; diğer taslak en/az metinleri (ADR-0034 aşama 4 dilimleri) ayrı bir gözden geçirme gerektirir.
- Sunucudan dönen Türkçe doğrulama hata metni (rengin kaydedilmesi reddedilirse) hâlâ çevrilmemiştir; çeviri hattına bağlandığında bu listeye eklenir.
