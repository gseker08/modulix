using Modulix.Core.Customization;
using Modulix.Modules.Crm;

namespace Modulix.Api.Customization;

/// <summary>
/// Hazır alt profiller (ADR-0041 K9): pilot (Makaroğlu) için söylenen üç faaliyet ile hava, demiryolu ve çok modlu taşıma. İçerik ürün önerisidir ve bir işletmeyle doğrulanmamıştır;
/// mevzuata bağlı hiçbir alan ya da kural içermez. Kara taşımacılığı, "Lojistik firması" sektör profilinin üç fırsat alanını taşır
/// (aynı adlı alan atlandığı için o profili uygulamış firmalar etkilenmez). Faaliyet kodları lojistik başlangıç paketindendir; katalogda yoksa
/// alt profil bağlanmadan eklenir.
/// </summary>
internal static class SubProfileStarters
{
    public static IEnumerable<SubProfileStarter> Create() =>
    [
        new(
            "kara-tasimaciligi",
            "Kara taşımacılığı",
            "Karayolu taşıma teklifleri için güzergâh, araç tipi ve yük tipi.",
            "karayolu-tasimaciligi",
            [
                Field(CrmModule.Entities.Opportunity, "Güzergâh", CustomFieldType.Text, showInList: true),
                Field(CrmModule.Entities.Opportunity, "Araç tipi", CustomFieldType.Choice, showInList: true, choices: ["Tır", "Kamyon", "Kamyonet", "Frigorifik"]),
                Field(CrmModule.Entities.Opportunity, "Yük tipi", CustomFieldType.Choice, choices: ["Paletli", "Dökme", "Soğuk zincir", "Parsiyel"]),
            ],
            [new("Taşıma türü", "Komple"), new("Taşıma türü", "Parsiyel"), new("Kapsam", "Yurt içi"), new("Kapsam", "Uluslararası")]),
        new(
            "gumruklu-antrepo",
            "Gümrüklü antrepo",
            "Antrepo hizmeti teklifleri için depolama süresi, gümrük işlemi ve ürün grubu.",
            "gumruklu-antrepo",
            [
                Field(CrmModule.Entities.Opportunity, "Depolama süresi (gün)", CustomFieldType.Number, showInList: true),
                Field(CrmModule.Entities.Opportunity, "Gümrük işlemi gerekli", CustomFieldType.YesNo),
                Field(CrmModule.Entities.Opportunity, "Ürün grubu", CustomFieldType.Text),
            ]),
        new(
            "deniz-tasimaciligi",
            "Deniz taşımacılığı",
            "Denizyolu taşıma teklifleri için limanlar, konteyner tipi ve teslim şekli.",
            "denizyolu-tasimaciligi",
            [
                Field(CrmModule.Entities.Opportunity, "Yükleme limanı", CustomFieldType.Text),
                Field(CrmModule.Entities.Opportunity, "Varış limanı", CustomFieldType.Text, showInList: true),
                Field(CrmModule.Entities.Opportunity, "Konteyner tipi", CustomFieldType.Choice, choices: ["20'", "40'", "40' HC", "Dökme"]),
                Field(CrmModule.Entities.Opportunity, "Teslim şekli (Incoterm)", CustomFieldType.Choice, choices: ["EXW", "FCA", "FOB", "CFR", "CIF", "CPT", "CIP", "DAP", "DPU", "DDP"]),
            ],
            [new("Taşıma türü", "Tam konteyner"), new("Taşıma türü", "Parsiyel konteyner"), new("Taşıma türü", "Dökme"), new("Kapsam", "Uluslararası")]),
        new(
            "hava-tasimaciligi",
            "Hava taşımacılığı",
            "Havayolu taşıma teklifleri için havalimanları, ücrete esas ağırlık ve kargo türü.",
            "havayolu-tasimaciligi",
            [
                Field(CrmModule.Entities.Opportunity, "Kalkış havalimanı", CustomFieldType.Text),
                Field(CrmModule.Entities.Opportunity, "Varış havalimanı", CustomFieldType.Text, showInList: true),
                Field(CrmModule.Entities.Opportunity, "Ücrete esas ağırlık (kg)", CustomFieldType.Number),
                Field(CrmModule.Entities.Opportunity, "Kargo türü", CustomFieldType.Choice, choices: ["Genel kargo", "Tehlikeli madde", "Soğuk zincir", "Değerli kargo"]),
            ],
            [new("Taşıma türü", "Genel kargo"), new("Taşıma türü", "Ekspres"), new("Kapsam", "Uluslararası")]),
        new(
            "demiryolu-tasimaciligi",
            "Demiryolu taşımacılığı",
            "Demiryolu taşıma teklifleri için istasyonlar, vagon tipi ve konteyner taşıması.",
            "demiryolu-tasimaciligi",
            [
                Field(CrmModule.Entities.Opportunity, "Yükleme istasyonu", CustomFieldType.Text),
                Field(CrmModule.Entities.Opportunity, "Varış istasyonu", CustomFieldType.Text, showInList: true),
                Field(CrmModule.Entities.Opportunity, "Vagon tipi", CustomFieldType.Choice, choices: ["Kapalı vagon", "Açık vagon", "Platform vagon", "Sarnıç vagon"]),
                Field(CrmModule.Entities.Opportunity, "Konteyner taşıması", CustomFieldType.YesNo),
            ],
            [new("Taşıma türü", "Blok tren"), new("Taşıma türü", "Tek vagon"), new("Kapsam", "Yurt içi"), new("Kapsam", "Uluslararası")]),

        // Çok modlu taşıma: taşıma organizasyonuna bağlıdır; firma iki ya da daha fazla taşıma modu seçtiğinde de önerilir.
        new(
            "multimodal-tasima",
            "Multimodal taşıma",
            "Birden fazla taşıma modunu birleştiren teklifler için modlar, ana mod ve aktarma noktası.",
            "tasima-organizasyonu",
            [
                Field(CrmModule.Entities.Opportunity, "Taşıma modları", CustomFieldType.MultiChoice, showInList: true, choices: ["Karayolu", "Denizyolu", "Havayolu", "Demiryolu"]),
                Field(CrmModule.Entities.Opportunity, "Ana taşıma modu", CustomFieldType.Choice, choices: ["Karayolu", "Denizyolu", "Havayolu", "Demiryolu"]),
                Field(CrmModule.Entities.Opportunity, "Aktarma noktası", CustomFieldType.Text),
                Field(CrmModule.Entities.Opportunity, "Kapıdan kapıya", CustomFieldType.YesNo),
            ],
            [new("Taşıma türü", "Multimodal"), new("Kapsam", "Yurt içi"), new("Kapsam", "Uluslararası")],
            SuggestedActivityCodes: ["karayolu-tasimaciligi", "denizyolu-tasimaciligi", "havayolu-tasimaciligi", "demiryolu-tasimaciligi"],
            SuggestedMinMatches: 2),
    ];

    private static SubProfileOptionInput Field(string entityKey, string label, CustomFieldType type, bool showInList = false, string[]? choices = null) =>
        new(SubProfileOptionKind.Field, entityKey, label, type, ShowInList: showInList, Choices: choices);
}
