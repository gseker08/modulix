namespace Modulix.Core.Contracts.Notes;

// Kayıt notu (ADR-0057): herhangi bir kayda bağlı, kendi ekleri olan ayrı not kaydı. Nota erişim bağlı kaydın erişimidir: kaydı okuyabilen notları görür,
// güncelleyebilen not ekler; notu yalnızca yazarı değiştirir ve siler. Notun ekleri genel dosya deposundadır (ADR-0040) ve nota (core.note) bağlanır.

/// <summary>Kayıt notu türü anahtarı.</summary>
public static class NoteEntities
{
    /// <summary>Kayıt boru hattındaki tür anahtarı (<c>/api/records/core.note</c>).</summary>
    public const string Note = "core.note";
}
