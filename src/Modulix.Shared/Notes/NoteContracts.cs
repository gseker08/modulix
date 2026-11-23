namespace Modulix.Shared.Notes;

/// <summary>
/// Kayıt notu (ADR-0057) anahtarı (arayüz için); sunucudaki <c>NoteEntities</c> ile aynıdır (birim testle denetlenir). Notlar genel kayıt uçlarından
/// (<c>/api/records/core.note?parentEntityKey=&amp;parentId=</c>) okunur ve yazılır; erişim bağlı kaydın erişimidir. Notun ekleri <c>/api/files</c> ile nota bağlanır.
/// </summary>
public static class NoteAccess
{
    public const string EntityKey = "core.note";

    public const int BodyMaxLength = 4000;
}
