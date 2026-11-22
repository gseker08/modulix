using Modulix.Core.Contracts.Localization;

namespace Modulix.Modules.Hr;

/// <summary>
/// İK modülünün sunucu iletileri (ADR-0034 §6, aşama 4 dilim 18d-5). Anahtarlar <c>hr.server.*</c>; Türkçe metin <c>tr.json</c>'daki
/// kalıpla aynıdır (birim testle denetlenir) ve HTTP sınırında isteğin diline çevrilir. Ortak iletiler (alan biçimleri, izin, eşzamanlılık)
/// <see cref="ServerMessages"/>'tadır.
/// </summary>
internal static class HrMessages
{
    /// <summary>Kayıt alanı biçimi.</summary>
    public static class Field
    {
        public static ServerMessage Moment =>
            ServerMessage.Of("hr.server.field.moment", "Tarih ve saat ISO 8601 biçiminde olmalıdır (ör. 2026-11-12T08:30:00+03:00).");

        public static ServerMessage MaxDecimals(int scale) =>
            ServerMessage.Of("hr.server.field.max-decimals", "En fazla {0} ondalık basamak girilebilir.", scale);

        public static ServerMessage IdList =>
            ServerMessage.Of("hr.server.field.id-list", "Kimlik listesi olmalıdır.");

        public static ServerMessage IdListItems =>
            ServerMessage.Of("hr.server.field.id-list-items", "Geçerli kayıt kimlikleri olmalıdır.");

        public static ServerMessage IdListDuplicate =>
            ServerMessage.Of("hr.server.field.id-list-duplicate", "Aynı kayıt bir kez seçilir.");

        public static ServerMessage IdListMax(int max) =>
            ServerMessage.Of("hr.server.field.id-list-max", "En çok {0} kayıt seçilir.", max);

        public static ServerMessage NotActiveMember =>
            ServerMessage.Of("hr.server.field.not-active-member", "Kullanıcı bu kuruluşun etkin üyesi değil.");
    }

    /// <summary>Bağlı kayıt alanlarında "bulunamadı" iletileri.</summary>
    public static class NotFound
    {
        public static ServerMessage Person =>
            ServerMessage.Of("hr.server.notfound.person", "Kişi bulunamadı.");

        public static ServerMessage Manager =>
            ServerMessage.Of("hr.server.notfound.manager", "Yönetici bulunamadı.");

        public static ServerMessage Employee =>
            ServerMessage.Of("hr.server.notfound.employee", "Çalışan bulunamadı.");

        public static ServerMessage ExpenseClaim =>
            ServerMessage.Of("hr.server.notfound.expense-claim", "Masraf beyanı bulunamadı.");

        public static ServerMessage Asset =>
            ServerMessage.Of("hr.server.notfound.asset", "Demirbaş bulunamadı.");

        public static ServerMessage Announcement =>
            ServerMessage.Of("hr.server.notfound.announcement", "Duyuru bulunamadı.");

        public static ServerMessage Survey =>
            ServerMessage.Of("hr.server.notfound.survey", "Anket bulunamadı.");

        public static ServerMessage Question =>
            ServerMessage.Of("hr.server.notfound.question", "Soru bulunamadı.");

        public static ServerMessage Response =>
            ServerMessage.Of("hr.server.notfound.response", "Yanıt bulunamadı.");

        public static ServerMessage Template =>
            ServerMessage.Of("hr.server.notfound.template", "Şablon bulunamadı.");

        public static ServerMessage Checklist =>
            ServerMessage.Of("hr.server.notfound.checklist", "Süreç bulunamadı.");

        public static ServerMessage Content =>
            ServerMessage.Of("hr.server.notfound.content", "İçerik bulunamadı.");

        public static ServerMessage Comment =>
            ServerMessage.Of("hr.server.notfound.comment", "Yorum bulunamadı.");
    }

    /// <summary>Çalışan kartı kayıtları.</summary>
    public static class Employee
    {
        public static ServerMessage PersonTaken =>
            ServerMessage.Of("hr.server.employee.person-taken", "Bu kişinin zaten bir çalışan kartı var.");

        public static ServerMessage NumberTaken =>
            ServerMessage.Of("hr.server.employee.number-taken", "Bu sicil numarası başka bir çalışanda kayıtlı.");

        public static ServerMessage UserTaken =>
            ServerMessage.Of("hr.server.employee.user-taken", "Bu kullanıcı başka bir çalışan kartına bağlı.");

        public static ServerMessage NationalIdInvalid =>
            ServerMessage.Of("hr.server.employee.national-id-invalid", "T.C. kimlik numarası 11 haneli olmalı ve denetim haneleri tutmalıdır.");

        public static ServerMessage NationalIdTaken =>
            ServerMessage.Of("hr.server.employee.national-id-taken", "Bu T.C. kimlik numarası başka bir çalışanda kayıtlı.");

        public static ServerMessage IbanInvalid =>
            ServerMessage.Of("hr.server.employee.iban-invalid", "IBAN TR ile başlayan 26 karakter olmalı ve denetim haneleri tutmalıdır.");

        public static ServerMessage TerminationDateRequired =>
            ServerMessage.Of("hr.server.employee.termination-date-required", "\"Ayrıldı\" durumundaki çalışanda ayrılış tarihi girilmelidir.");

        public static ServerMessage TerminationBeforeHire =>
            ServerMessage.Of("hr.server.employee.termination-before-hire", "Ayrılış tarihi işe giriş tarihinden önce olamaz.");

        public static ServerMessage BirthAfterHire =>
            ServerMessage.Of("hr.server.employee.birth-after-hire", "Doğum tarihi işe giriş tarihinden önce olmalıdır.");

        public static ServerMessage OwnManager =>
            ServerMessage.Of("hr.server.employee.own-manager", "Çalışan kendi yöneticisi olamaz.");

        public static ServerMessage ManagerCycle =>
            ServerMessage.Of("hr.server.employee.manager-cycle", "Seçilen yönetici bu çalışanın astı; organizasyon şemasında döngü oluşur.");

        public static ServerMessage UnitNameMissing(string name) =>
            ServerMessage.Of("hr.server.employee.unit-name-missing", "\"{0}\" adlı etkin bir departman yok; departmanı Yönetim › Departmanlar'dan açın.", name);

        public static ServerMessage UnitNameAmbiguous(string name) =>
            ServerMessage.Of("hr.server.employee.unit-name-ambiguous", "\"{0}\" birden çok departmana uyuyor; ağaç yoluyla yazın (ör. \"Merkez › Satış\").", name);

        public static ServerMessage UnitLockedByUser =>
            ServerMessage.Of("hr.server.employee.unit-locked-by-user", "Kullanıcı hesabı bağlı çalışanın departmanı kullanıcının departmanıdır; Yönetim › Kullanıcılar'dan departman değişikliğiyle değiştirilir.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("hr.server.employee.no-delete", "Çalışan kartları silinmez; ayrılan çalışanın durumunu \"Ayrıldı\" yapın ve ayrılış tarihini girin.");
    }

    /// <summary>Görev geçmişi kayıtları.</summary>
    public static class Position
    {
        public static ServerMessage CreateBlocked =>
            ServerMessage.Of("hr.server.position.create-blocked", "Görev geçmişi çalışan kartı güncellenince yazılır.");

        public static ServerMessage Immutable =>
            ServerMessage.Of("hr.server.position.immutable", "Görev geçmişi değiştirilmez; çalışan kartını güncelleyin.");

        public static ServerMessage EffectiveAfterStart(string start) =>
            ServerMessage.Of("hr.server.position.effective-after-start", "Geçerlilik günü önceki görevin başlangıcından ({0}) sonra olmalıdır.", start);

        public static ServerMessage EffectiveBeforeTermination =>
            ServerMessage.Of("hr.server.position.effective-before-termination", "Geçerlilik günü ayrılış tarihinden sonra olamaz.");
    }

    /// <summary>Duyuru ve anket hedef kitlesi.</summary>
    public static class Audience
    {
        public static ServerMessage UnitsMax(int max) =>
            ServerMessage.Of("hr.server.audience.units-max", "En çok {0} departman seçilir.", max);

        public static ServerMessage UsersMax(int max) =>
            ServerMessage.Of("hr.server.audience.users-max", "En çok {0} kişi seçilir.", max);

        public static ServerMessage UserNotActiveMember =>
            ServerMessage.Of("hr.server.audience.user-not-active-member", "Seçilen kullanıcı bu kuruluşun etkin üyesi değil.");
    }

    /// <summary>Departman seçimi.</summary>
    public static class Unit
    {
        public static ServerMessage NotFound =>
            ServerMessage.Of("hr.server.unit.not-found", "Departman bulunamadı.");

        public static ServerMessage Closed =>
            ServerMessage.Of("hr.server.unit.closed", "Kapalı departman seçilemez.");
    }

    /// <summary>Departman eşleme.</summary>
    public static class Mapping
    {
        public static ServerMessage UnitsReadDenied =>
            ServerMessage.Of("hr.server.mapping.units-read-denied", "Departmanları görme yetkiniz yok.");

        public static ServerMessage Denied =>
            ServerMessage.Of("hr.server.mapping.denied", "Departman eşleme yetkiniz yok.");
    }

    /// <summary>Genel uçtan yazılamayan ya da değiştirilemeyen kayıt iletileri (Yalnızca eylem uçlarıyla yazılan kayıtlar).</summary>
    public static class Blocked
    {
        public static ServerMessage AnnouncementReadCreate =>
            ServerMessage.Of("hr.server.blocked.announcement-read-create", "Okundu bilgisi duyuru açılınca yazılır.");

        public static ServerMessage AnnouncementReadImmutable =>
            ServerMessage.Of("hr.server.blocked.announcement-read-immutable", "Okundu bilgisi değiştirilmez.");

        public static ServerMessage SurveyResponseCreate =>
            ServerMessage.Of("hr.server.blocked.survey-response-create", "Yanıt /api/hr/surveys/{id}/responses ile gönderilir.");

        public static ServerMessage SurveyResponseImmutable =>
            ServerMessage.Of("hr.server.blocked.survey-response-immutable", "Gönderilmiş yanıt değiştirilmez.");

        public static ServerMessage ChecklistCreate =>
            ServerMessage.Of("hr.server.blocked.checklist-create", "Süreç /api/hr/employee-checklists/start ile başlatılır.");

        public static ServerMessage ChecklistUpdate =>
            ServerMessage.Of("hr.server.blocked.checklist-update", "Süreç değiştirilmez; görevleri tamamlayın ya da süreci iptal edin.");

        public static ServerMessage ChecklistTaskCreate =>
            ServerMessage.Of("hr.server.blocked.checklist-task-create", "Görevler süreç başlatılırken şablondan oluşur.");

        public static ServerMessage ChecklistTaskUpdate =>
            ServerMessage.Of("hr.server.blocked.checklist-task-update", "Görev değiştirilmez; tamamlayın, atlayın ya da yeniden açın.");

        public static ServerMessage CommentCreate =>
            ServerMessage.Of("hr.server.blocked.comment-create", "Yorum, yorum yazma eylemiyle eklenir.");

        public static ServerMessage CommentUpdate =>
            ServerMessage.Of("hr.server.blocked.comment-update", "Yorumun metni değiştirilmez; içeriği yöneten gizleyebilir.");

        public static ServerMessage LikeCreate =>
            ServerMessage.Of("hr.server.blocked.like-create", "Beğeni, beğen eylemiyle eklenir.");

        public static ServerMessage LikeUpdate =>
            ServerMessage.Of("hr.server.blocked.like-update", "Beğeni değiştirilmez.");

        public static ServerMessage AssetUpdate =>
            ServerMessage.Of("hr.server.blocked.asset-update", "Zimmet değiştirilemez; iade edip yeniden zimmetleyin.");

        public static ServerMessage LeaveUpdate =>
            ServerMessage.Of("hr.server.blocked.leave-update", "İzin talebi değiştirilemez; talebi iptal edip yenisini açın.");
    }

    /// <summary>Eylem uçları için ortak iletiler.</summary>
    public static class Workflow
    {
        public static ServerMessage UnknownAction =>
            ServerMessage.Of("hr.server.workflow.unknown-action", "Bilinmeyen eylem.");

        public static ServerMessage NotAllowed =>
            ServerMessage.Of("hr.server.workflow.not-allowed", "Bu eylem için yetkiniz yok.");

        public static ServerMessage NotNow =>
            ServerMessage.Of("hr.server.workflow.not-now", "Bu eylem şu an yapılamaz.");

        public static ServerMessage ReasonRequired =>
            ServerMessage.Of("hr.server.workflow.reason-required", "Gerekçe zorunludur.");

        public static ServerMessage ReasonTooLong =>
            ServerMessage.Of("hr.server.workflow.reason-too-long", "Gerekçe çok uzun.");

        public static ServerMessage ExplanationRequired =>
            ServerMessage.Of("hr.server.workflow.explanation-required", "Açıklama zorunludur.");

        public static ServerMessage TextTooLong =>
            ServerMessage.Of("hr.server.workflow.text-too-long", "Metin çok uzun.");
    }


    /// <summary>İzin hakkı ve talebi.</summary>
    public static class Leave
    {
        public static ServerMessage EntitlementTaken =>
            ServerMessage.Of("hr.server.leave.entitlement-taken", "Bu çalışanın bu yıl için izin hakkı zaten tanımlı.");

        public static ServerMessage EntitledBelowUsed(string used) =>
            ServerMessage.Of("hr.server.leave.entitled-below-used", "Hak ve devreden gün toplamı kullanılan günden ({0}) az olamaz.", used);

        public static ServerMessage EntitlementDeleteUsed =>
            ServerMessage.Of("hr.server.leave.entitlement-delete-used", "İzin kullanılmış hak silinemez; hak gününü düzeltin.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("hr.server.leave.no-delete", "İzin talepleri silinmez; talebi iptal edin.");

        public static ServerMessage EmployeeTerminated =>
            ServerMessage.Of("hr.server.leave.employee-terminated", "Ayrılmış çalışana izin talebi açılamaz.");

        public static ServerMessage OwnOnly =>
            ServerMessage.Of("hr.server.leave.own-only", "Yalnızca kendiniz için izin talebi açabilirsiniz.");

        public static ServerMessage EndBeforeStart =>
            ServerMessage.Of("hr.server.leave.end-before-start", "Bitiş tarihi başlangıçtan önce olamaz.");

        public static ServerMessage SingleYear =>
            ServerMessage.Of("hr.server.leave.single-year", "Talep tek takvim yılı içinde kalmalıdır; yıl sonunu aşan izni iki talebe bölün.");

        public static ServerMessage HalfDaySingle =>
            ServerMessage.Of("hr.server.leave.half-day-single", "Yarım gün yalnızca tek günlük talepte seçilebilir.");

        public static ServerMessage NoWorkDays =>
            ServerMessage.Of("hr.server.leave.no-work-days", "Seçilen aralıkta iş günü yok (hafta sonu ya da resmî tatil).");

        public static ServerMessage Overlap =>
            ServerMessage.Of("hr.server.leave.overlap", "Bu tarihlerde bekleyen ya da onaylanmış başka bir izin talebi var.");

        public static ServerMessage NoEntitlement(int year) =>
            ServerMessage.Of("hr.server.leave.no-entitlement", "{0} yılı için yıllık izin hakkı tanımlanmamış; önce hakkı girin.", year);

        public static ServerMessage InsufficientBalance(string remaining, string requested) =>
            ServerMessage.Of("hr.server.leave.insufficient-balance", "Yetersiz izin bakiyesi: kalan {0} gün, istenen {1} gün.", remaining, requested);

        public static ServerMessage PreviewRangeTitle =>
            ServerMessage.Of("hr.server.leave.preview-range-title", "Tarih aralığı geçersiz.");

        public static ServerMessage PreviewRange =>
            ServerMessage.Of("hr.server.leave.preview-range", "Bitiş başlangıçtan önce olamaz, talep tek yıl içinde kalır; yarım gün yalnızca tek günlük talepte.");

        public static ServerMessage OwnDecision =>
            ServerMessage.Of("hr.server.leave.own-decision", "Kendi izin talebinizi onaylayamaz ya da reddedemezsiniz.");

        public static ServerMessage DecideOnlyPending =>
            ServerMessage.Of("hr.server.leave.decide-only-pending", "Yalnızca onay bekleyen talep onaylanır ya da reddedilir.");

        public static ServerMessage CancelByHrOnly =>
            ServerMessage.Of("hr.server.leave.cancel-by-hr-only", "Onaylanmış izni yalnızca İK iptal edebilir; İK'ya başvurun.");

        public static ServerMessage CancelNotPossible =>
            ServerMessage.Of("hr.server.leave.cancel-not-possible", "Reddedilmiş ya da iptal edilmiş talep iptal edilemez.");
    }


    /// <summary>Masraf beyanı ve satırı.</summary>
    public static class Expense
    {
        public static ServerMessage NoDelete =>
            ServerMessage.Of("hr.server.expense.no-delete", "Masraf beyanları silinmez; beyanı iptal edin.");

        public static ServerMessage CurrencyFormat =>
            ServerMessage.Of("hr.server.expense.currency-format", "Para birimi üç harfli kod olmalıdır (ör. TRY, EUR).");

        public static ServerMessage EmployeeTerminated =>
            ServerMessage.Of("hr.server.expense.employee-terminated", "Ayrılmış çalışana masraf beyanı açılamaz.");

        public static ServerMessage OwnOnly =>
            ServerMessage.Of("hr.server.expense.own-only", "Yalnızca kendiniz için masraf beyanı açabilirsiniz.");

        public static ServerMessage DraftOnlyEdit =>
            ServerMessage.Of("hr.server.expense.draft-only-edit", "Yalnızca taslak beyan düzenlenebilir; onaylayan beyanı düzeltme için iade edebilir.");

        public static ServerMessage LineCurrencyLocked =>
            ServerMessage.Of("hr.server.expense.line-currency-locked", "Satırı olan beyanın para birimi değiştirilemez.");

        public static ServerMessage DateFuture =>
            ServerMessage.Of("hr.server.expense.date-future", "Masraf tarihi ileri bir gün olamaz.");

        public static ServerMessage VatOverGross =>
            ServerMessage.Of("hr.server.expense.vat-over-gross", "KDV tutarı, KDV dahil tutardan büyük olamaz.");

        public static ServerMessage LinesDraftOnly =>
            ServerMessage.Of("hr.server.expense.lines-draft-only", "Satırlar yalnızca taslak beyanda eklenir, değiştirilir ya da silinir.");

        public static ServerMessage OwnDecision =>
            ServerMessage.Of("hr.server.expense.own-decision", "Kendi masraf beyanınıza karar veremezsiniz.");

        public static ServerMessage DecideOnlySubmitted =>
            ServerMessage.Of("hr.server.expense.decide-only-submitted", "Yalnızca onaya gönderilmiş beyana karar verilir.");

        public static ServerMessage OwnPay =>
            ServerMessage.Of("hr.server.expense.own-pay", "Kendi beyanınızın ödemesini işaretleyemezsiniz.");

        public static ServerMessage PayOnlyApproved =>
            ServerMessage.Of("hr.server.expense.pay-only-approved", "Yalnızca onaylanmış beyan ödendi olarak işaretlenir.");

        public static ServerMessage SubmitOnlyDraft =>
            ServerMessage.Of("hr.server.expense.submit-only-draft", "Yalnızca taslak beyan onaya gönderilir.");

        public static ServerMessage SubmitNeedsLine =>
            ServerMessage.Of("hr.server.expense.submit-needs-line", "Onaya göndermeden önce en az bir masraf satırı ekleyin.");

        public static ServerMessage CancelOnlyDraftOrPending =>
            ServerMessage.Of("hr.server.expense.cancel-only-draft-or-pending", "Yalnızca taslak ya da onay bekleyen beyan iptal edilir.");

        public static ServerMessage NotOwnerEdit =>
            ServerMessage.Of("hr.server.expense.not-owner-edit", "Yalnızca kendi masraf beyanınızı düzenleyebilirsiniz.");

        public static ServerMessage FilesDraftOnly =>
            ServerMessage.Of("hr.server.expense.files-draft-only", "Fiş ve fatura yalnızca taslak beyanda eklenir ya da silinir.");
    }


    /// <summary>Demirbaş ve zimmet.</summary>
    public static class Asset
    {
        public static ServerMessage TagTaken =>
            ServerMessage.Of("hr.server.asset.tag-taken", "Bu envanter numarası başka bir demirbaşta kayıtlı.");

        public static ServerMessage StatusViaAssignment =>
            ServerMessage.Of("hr.server.asset.status-via-assignment", "\"Zimmetli\" durumu yalnızca zimmetle gelir; demirbaşı çalışana zimmetleyin.");

        public static ServerMessage StatusAssignedLocked =>
            ServerMessage.Of("hr.server.asset.status-assigned-locked", "Zimmetteki demirbaşın durumu iade ya da kayıp kaydıyla değişir.");

        public static ServerMessage CurrencyFormat =>
            ServerMessage.Of("hr.server.asset.currency-format", "Para birimi üç harfli kod olmalıdır (ör. TRY).");

        public static ServerMessage CurrencyWithPrice =>
            ServerMessage.Of("hr.server.asset.currency-with-price", "Alış tutarıyla birlikte para birimi girilmelidir.");

        public static ServerMessage HasHistory =>
            ServerMessage.Of("hr.server.asset.has-history", "Zimmet geçmişi olan demirbaş silinmez; durumunu \"Hurda\" yapın.");

        public static ServerMessage AssignmentNoDelete =>
            ServerMessage.Of("hr.server.asset.assignment-no-delete", "Zimmet kayıtları silinmez; demirbaşı iade alın.");

        public static ServerMessage AlreadyAssigned =>
            ServerMessage.Of("hr.server.asset.already-assigned", "Demirbaş başka bir çalışana zimmetli; önce iade alın.");

        public static ServerMessage NotInStock =>
            ServerMessage.Of("hr.server.asset.not-in-stock", "Yalnızca depodaki demirbaş zimmetlenir (bakımda, hurda ya da kayıp değil).");

        public static ServerMessage AssignTerminated =>
            ServerMessage.Of("hr.server.asset.assign-terminated", "Ayrılmış çalışana zimmet verilmez.");

        public static ServerMessage AssignedFuture =>
            ServerMessage.Of("hr.server.asset.assigned-future", "Teslim tarihi ileri bir gün olamaz.");

        public static ServerMessage ExpectedBeforeAssigned =>
            ServerMessage.Of("hr.server.asset.expected-before-assigned", "Beklenen iade tarihi teslimden önce olamaz.");

        public static ServerMessage AckClosed =>
            ServerMessage.Of("hr.server.asset.ack-closed", "Kapanmış zimmet onaylanmaz.");

        public static ServerMessage AckAlready =>
            ServerMessage.Of("hr.server.asset.ack-already", "Teslim zaten onaylandı.");

        public static ServerMessage AlreadyClosed =>
            ServerMessage.Of("hr.server.asset.already-closed", "Zimmet zaten kapanmış.");
    }

    /// <summary>İşe giriş/çıkış süreci ve şablonları.</summary>
    public static class Checklist
    {
        public static ServerMessage StartNotAllowed =>
            ServerMessage.Of("hr.server.checklist.start-not-allowed", "Süreç başlatma yetkiniz yok.");

        public static ServerMessage CancelledLocked =>
            ServerMessage.Of("hr.server.checklist.cancelled-locked", "İptal edilmiş sürecin görevi değişmez.");

        public static ServerMessage TaskAlreadyClosed =>
            ServerMessage.Of("hr.server.checklist.task-already-closed", "Görev zaten kapanmış.");

        public static ServerMessage TaskAlreadyOpen =>
            ServerMessage.Of("hr.server.checklist.task-already-open", "Görev zaten açık.");

        public static ServerMessage OpenAssets(int count) =>
            ServerMessage.Of("hr.server.checklist.open-assets", "Çalışanın {0} açık zimmeti var; önce demirbaşları iade alın.", count);

        public static ServerMessage OnlyOpenCancel =>
            ServerMessage.Of("hr.server.checklist.only-open-cancel", "Yalnızca açık süreç iptal edilir.");

        public static ServerMessage TemplateNameTaken =>
            ServerMessage.Of("hr.server.checklist.template-name-taken", "Bu adla aynı türde başka bir şablon var.");

        public static ServerMessage TemplateInUse =>
            ServerMessage.Of("hr.server.checklist.template-in-use", "Bu şablonla süreç başlatılmış; şablon silinmez, pasife alın.");

        public static ServerMessage AssetReturnOffboardingOnly =>
            ServerMessage.Of("hr.server.checklist.asset-return-offboarding-only", "\"Zimmet iadesi\" görevi yalnızca işten çıkış şablonunda olur.");

        public static ServerMessage NoDelete =>
            ServerMessage.Of("hr.server.checklist.no-delete", "Süreçler silinmez; süreci iptal edin.");

        public static ServerMessage TaskNoDelete =>
            ServerMessage.Of("hr.server.checklist.task-no-delete", "Görevler silinmez; atlayın.");

        public static ServerMessage TemplateInactive =>
            ServerMessage.Of("hr.server.checklist.template-inactive", "Pasif şablonla süreç başlatılamaz.");

        public static ServerMessage TemplateEmpty =>
            ServerMessage.Of("hr.server.checklist.template-empty", "Şablonun hiç adımı yok.");

        public static ServerMessage EmployeeTerminated =>
            ServerMessage.Of("hr.server.checklist.employee-terminated", "Ayrılmış çalışana işe giriş süreci başlatılamaz.");

        public static ServerMessage OpenOnboarding =>
            ServerMessage.Of("hr.server.checklist.open-onboarding", "Bu çalışanın açık bir işe giriş süreci var.");

        public static ServerMessage OpenOffboarding =>
            ServerMessage.Of("hr.server.checklist.open-offboarding", "Bu çalışanın açık bir işten çıkış süreci var.");
    }

    /// <summary>Duyuru, blog ve içerik etkileşimleri (yorum, beğeni).</summary>
    public static class Content
    {
        public static ServerMessage InvalidExpiry =>
            ServerMessage.Of("hr.server.content.invalid-expiry", "Bitiş günü başlangıç gününden önce olamaz.");

        public static ServerMessage NoActiveUser =>
            ServerMessage.Of("hr.server.content.no-active-user", "Aktif kullanıcı yok.");

        public static ServerMessage AlreadyLiked =>
            ServerMessage.Of("hr.server.content.already-liked", "Bu içeriği zaten beğendiniz.");

        public static ServerMessage LikeRemoveViaAction =>
            ServerMessage.Of("hr.server.content.like-remove-via-action", "Beğeni, beğen eylemiyle kaldırılır.");

        public static ServerMessage ModerateAction =>
            ServerMessage.Of("hr.server.content.moderate-action", "Eylem hide, show ya da delete olmalıdır.");

        public static ServerMessage DeleteOwnOnly =>
            ServerMessage.Of("hr.server.content.delete-own-only", "Yalnızca kendi yorumunuzu silebilirsiniz.");

        public static ServerMessage HideNeedsManage =>
            ServerMessage.Of("hr.server.content.hide-needs-manage", "Yorumu gizlemek için içeriği yönetme yetkisi gerekir.");
    }

    /// <summary>Blog yazıları ve onay akışı.</summary>
    public static class Blog
    {
        public static ServerMessage Locked =>
            ServerMessage.Of("hr.server.blog.locked", "Onay bekleyen ya da yayındaki yazı değiştirilemez; yazı geri çevrilirse düzeltilir.");

        public static ServerMessage OwnFilesOnly =>
            ServerMessage.Of("hr.server.blog.own-files-only", "Yalnızca kendi yazınızın eklerini değiştirebilirsiniz.");

        public static ServerMessage PublishNotAllowed =>
            ServerMessage.Of("hr.server.blog.publish-not-allowed", "Yazıyı yayınlama yetkiniz yok; yazıyı onaya gönderin.");

        public static ServerMessage OwnPostOnly =>
            ServerMessage.Of("hr.server.blog.own-post-only", "Yalnızca kendi yazınızı değiştirebilir ve silebilirsiniz.");

        public static ServerMessage SubmitOwnOnly =>
            ServerMessage.Of("hr.server.blog.submit-own-only", "Yalnızca kendi yazınızı onaya gönderebilirsiniz.");

        public static ServerMessage SubmitState =>
            ServerMessage.Of("hr.server.blog.submit-state", "Yalnızca taslak ya da geri çevrilen yazı onaya gönderilir.");

        public static ServerMessage DecideState =>
            ServerMessage.Of("hr.server.blog.decide-state", "Yalnızca onay bekleyen yazı için karar verilir.");

        public static ServerMessage ReturnReasonRequired =>
            ServerMessage.Of("hr.server.blog.return-reason-required", "Geri çevirme gerekçesi zorunludur.");

        public static ServerMessage ReviewNoteMax(int max) =>
            ServerMessage.Of("hr.server.blog.review-note-max", "Gerekçe en çok {0} karakter olabilir.", max);
    }

    /// <summary>Anketler, sorular ve yanıtlar.</summary>
    public static class Survey
    {
        public static ServerMessage NoQuestions =>
            ServerMessage.Of("hr.server.survey.no-questions", "Sorusu olmayan anket yanıta açılmaz.");

        public static ServerMessage ClosingDatePassed =>
            ServerMessage.Of("hr.server.survey.closing-date-passed", "Kapanış günü geçmiş; önce kapanış gününü değiştirin.");

        public static ServerMessage HasResponses =>
            ServerMessage.Of("hr.server.survey.has-responses", "Yanıtı olan anket silinmez; kapatın.");

        public static ServerMessage QuestionsLocked =>
            ServerMessage.Of("hr.server.survey.questions-locked", "Yanıta açılmış anketin soruları değiştirilmez.");

        public static ServerMessage OptionsInvalid(int max, int length) =>
            ServerMessage.Of("hr.server.survey.options-invalid", "Seçim sorusuna satır başına bir tane olmak üzere 2–{0} farklı seçenek yazın (her biri en çok {1} karakter).", max, length);

        public static ServerMessage AnonymousNoLink =>
            ServerMessage.Of("hr.server.survey.anonymous-no-link", "Kimliği gizli anketin cevabı katılıma bağlanmaz.");

        public static ServerMessage OpenNeedsLink =>
            ServerMessage.Of("hr.server.survey.open-needs-link", "Kimliği açık anketin cevabı katılıma bağlanır.");

        public static ServerMessage AlreadyOpen =>
            ServerMessage.Of("hr.server.survey.already-open", "Anket zaten yanıta açık.");

        public static ServerMessage OnlyOpenClose =>
            ServerMessage.Of("hr.server.survey.only-open-close", "Yalnızca yanıta açık anket kapatılır.");

        public static ServerMessage RespondNotAllowed =>
            ServerMessage.Of("hr.server.survey.respond-not-allowed", "Anket yanıtlama yetkiniz yok.");

        public static ServerMessage ResultsNotAllowed =>
            ServerMessage.Of("hr.server.survey.results-not-allowed", "Anket sonuçlarını görme yetkiniz yok.");

        public static ServerMessage NotOpenForYou =>
            ServerMessage.Of("hr.server.survey.not-open-for-you", "Bu anket şu an size yönelik yanıta açık değil.");

        public static ServerMessage AlreadyAnswered =>
            ServerMessage.Of("hr.server.survey.already-answered", "Bu anketi daha önce yanıtladınız.");

        public static ServerMessage ResponseErrors =>
            ServerMessage.Of("hr.server.survey.response-errors", "Yanıtta hatalar var.");

        public static ServerMessage AnonymousMinimum(int min) =>
            ServerMessage.Of("hr.server.survey.anonymous-minimum", "Kimliği gizli ankette sonuçlar en az {0} yanıttan sonra gösterilir.", min);

        public static ServerMessage UnknownQuestion =>
            ServerMessage.Of("hr.server.survey.unknown-question", "Bu ankette böyle bir soru yok.");

        public static ServerMessage AnswerOnce =>
            ServerMessage.Of("hr.server.survey.answer-once", "Soru bir kez cevaplanır.");

        public static ServerMessage RatingRange =>
            ServerMessage.Of("hr.server.survey.rating-range", "Puan 1 ile 5 arasında olmalıdır.");

        public static ServerMessage ChooseOne =>
            ServerMessage.Of("hr.server.survey.choose-one", "Seçeneklerden birini seçin.");

        public static ServerMessage TextMax(int max) =>
            ServerMessage.Of("hr.server.survey.text-max", "En çok {0} karakter yazın.", max);

        public static ServerMessage ChoiceOnce =>
            ServerMessage.Of("hr.server.survey.choice-once", "Bir seçenek bir kez seçilir.");

        public static ServerMessage ChooseFrom =>
            ServerMessage.Of("hr.server.survey.choose-from", "Seçeneklerden seçin.");
    }
}
