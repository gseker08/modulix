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
}
