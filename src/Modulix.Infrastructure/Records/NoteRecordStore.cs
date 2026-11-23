using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Modules;
using Modulix.Core.Contracts.Notes;
using Modulix.Core.Contracts.Records;
using Modulix.Core.Contracts.Tenancy;
using Modulix.Core.Notes;
using Modulix.Core.Records;
using Modulix.Infrastructure.Persistence;

namespace Modulix.Infrastructure.Records;

/// <summary>
/// Kayıt notları (ADR-0057): <c>core.note</c>. Nota erişim bağlı kaydın erişimidir: bağlı kaydı (kapsam dahil) okuyabilen notlarını görür; bağlı
/// kayıtta güncelleme izni olan, kaydın yazma kuralı (<see cref="IRecordWriteGuard"/>) ve modül lisansı yazılabilir evrede iken not ekler. Notu yalnızca
/// yazarı değiştirir ve siler. Not ekleri genel dosya deposundadır (ADR-0040): dosya ekleme ve silme de aynı kuralı (<see cref="WriteDeniedAsync"/>) sorar.
/// Bağlı kaydı görünmeyen notun kendisi de "bulunamadı"dır; varlığı sızmaz.
/// </summary>
internal sealed class NoteRecordStore(
    ModulixDbContext db,
    IRecordAccess access,
    IModuleLicense license,
    ITenantContextAccessor tenantAccessor,
    IServiceProvider services) : IRecordStore, IRecordWriteGuard
{
    private static readonly string[] Writable = ["parentEntityKey", "parentId", "body"];

    public string EntityKey => NoteEntities.Note;

    /// <summary>Çekirdeğin kayıt türü: her tenant için açıktır; erişimi bağlı kaydın erişimi belirler.</summary>
    public string FeatureKey => EntityKey;

    // Kayıt deposu listesi ve boru hattı bu depoyu da içerdiği için yapıcıda değil, kullanımda alınır.
    private IRecordStoreRegistry Registry => services.GetRequiredService<IRecordStoreRegistry>();

    private IRecordPipeline Pipeline => services.GetRequiredService<IRecordPipeline>();

    public IReadOnlyList<RecordFieldError> Validate(RecordOperation operation, JsonObject values)
    {
        var errors = new List<RecordFieldError>();
        foreach (var (name, node) in values)
        {
            if (!Writable.Contains(name, StringComparer.Ordinal))
            {
                errors.Add(new RecordFieldError(name, "Bilinmeyen alan."));
            }
            else if (Parse(name, node).Error is { } error)
            {
                errors.Add(new RecordFieldError(name, error));
            }
        }

        if (operation == RecordOperation.Create)
        {
            errors.AddRange(Writable
                .Where(f => !values.ContainsKey(f) || IsEmpty(values[f]))
                .Select(f => new RecordFieldError(f, ServerMessages.Validation.Required)));
        }

        return errors;
    }

    public async Task<RecordData?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var note = await db.RecordNotes.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
        return note is null || !await ParentReadableAsync(note.ParentEntityKey, note.ParentId, cancellationToken) ? null : ToData(note);
    }

    public async Task<RecordPage> QueryAsync(RecordQuery query, CancellationToken cancellationToken)
    {
        string? parentKey = null;
        Guid? parentId = null;
        var descending = true;
        var errors = new List<RecordFieldError>();
        foreach (var (name, value) in query.Filters ?? new Dictionary<string, string>())
        {
            switch (name)
            {
                case "parentEntityKey":
                    parentKey = value;
                    break;
                case "parentId" when Guid.TryParse(value, out var id):
                    parentId = id;
                    break;
                case "sort" when value is "createdAt" or "-createdAt":
                    descending = value.StartsWith('-');
                    break;
                case "parentId" or "sort":
                    errors.Add(new RecordFieldError(name, name == "sort" ? "Geçersiz sıralama." : "Geçersiz filtre değeri."));
                    break;
                default:
                    errors.Add(new RecordFieldError(name, "Bilinmeyen filtre."));
                    break;
            }
        }

        // Notlar bağlı kayıt olmadan listelenmez: erişim bağlı kaydın erişimidir.
        errors.AddRange(new[] { ("parentEntityKey", parentKey is null), ("parentId", parentId is null) }
            .Where(m => m.Item2 && !errors.Any(e => e.Field == m.Item1))
            .Select(m => new RecordFieldError(m.Item1, ServerMessages.Validation.Required)));
        if (errors.Count > 0)
        {
            throw new RecordValidationException(errors);
        }

        // Bağlı kaydı göremeyene boş sayfa: kaydın ya da notların varlığı sızmaz.
        if (!await ParentReadableAsync(parentKey!, parentId!.Value, cancellationToken))
        {
            return new RecordPage([], query.Page, query.PageSize, 0);
        }

        var source = db.RecordNotes.AsNoTracking().Where(n => n.ParentEntityKey == parentKey && n.ParentId == parentId);
        if (query.Search is { } term)
        {
            source = source.Where(n => n.Body.Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);
        var ordered = descending ? source.OrderByDescending(n => n.CreatedAt) : source.OrderBy(n => n.CreatedAt);
        var items = await ordered.ThenBy(n => n.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new RecordPage([.. items.Select(ToData)], query.Page, query.PageSize, total);
    }

    public async Task<RecordData> CreateAsync(JsonObject values, CancellationToken cancellationToken)
    {
        var parentKey = (string)Parse("parentEntityKey", values["parentEntityKey"]).Value!;
        var parentId = (Guid)Parse("parentId", values["parentId"]).Value!;
        if (parentKey == EntityKey || Registry.Find(parentKey) is null)
        {
            throw Invalid("parentEntityKey", "Bu kayıt türüne not eklenemez.");
        }

        if (!await ParentReadableAsync(parentKey, parentId, cancellationToken))
        {
            // Başka tenant'ın ya da kapsam dışı kaydı da "bulunamadı" sayılır; varlığı belli edilmez.
            throw Invalid("parentId", "Kayıt bulunamadı.");
        }

        await EnsureParentWritableAsync(parentKey, parentId, cancellationToken);
        var note = new RecordNote
        {
            ParentEntityKey = parentKey,
            ParentId = parentId,
            Body = (string)Parse("body", values["body"]).Value!,
            CreatedByUserId = tenantAccessor.Current?.UserId,
        };
        db.RecordNotes.Add(note);
        return ToData(note);
    }

    public async Task<RecordData?> UpdateAsync(Guid id, JsonObject values, long? expectedVersion, CancellationToken cancellationToken)
    {
        if (await AuthoredAsync(id, cancellationToken) is not { } note)
        {
            return null;
        }

        EnsureVersion(note, expectedVersion);
        var errors = new List<RecordFieldError>();
        foreach (var (name, node) in values)
        {
            var value = Parse(name, node).Value;
            switch (name)
            {
                case "parentEntityKey" or "parentId":
                    var current = name == "parentId" ? note.ParentId.ToString() : note.ParentEntityKey;
                    if (value is not null && !string.Equals(value.ToString(), current, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(new RecordFieldError(name, "Oluşturulduktan sonra değiştirilemez."));
                    }

                    break;
                case "body":
                    if (value is string body && body != note.Body)
                    {
                        note.Body = body;
                        note.Touch();
                    }

                    break;
            }
        }

        if (errors.Count > 0)
        {
            throw new RecordValidationException(errors);
        }

        return ToData(note);
    }

    public async Task<bool> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        if (await AuthoredAsync(id, cancellationToken) is not { } note)
        {
            return false;
        }

        // Ekli dosyaların kaydı "kayıt silindi" olayıyla (core.file-cleanup, ADR-0040 K8) temizlenir.
        EnsureVersion(note, expectedVersion);
        db.RecordNotes.Remove(note);
        return true;
    }

    /// <summary>Nota dosya eklemek ve silmek (ADR-0040) notu değiştirmekle aynı kuraldır: yazarı olmak, bağlı kayıtta güncelleme izni ve yazılabilir lisans.</summary>
    public async Task<string?> WriteDeniedAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await AuthoredAsync(id, cancellationToken) is null ? "Not bulunamadı." : null;
        }
        catch (RecordAccessDeniedException denied)
        {
            return denied.Message;
        }
    }

    /// <summary>Bağlı kaydı okunabilir not; yazarı değilse, güncelleme izni ya da yazılabilir lisans yoksa yetkisiz. Bağlı kayıt görünmüyorsa null.</summary>
    private async Task<RecordNote?> AuthoredAsync(Guid id, CancellationToken cancellationToken)
    {
        var note = await db.RecordNotes.SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (note is null || !await ParentReadableAsync(note.ParentEntityKey, note.ParentId, cancellationToken))
        {
            return null;
        }

        if (note.CreatedByUserId is null || note.CreatedByUserId != tenantAccessor.Current?.UserId)
        {
            throw new RecordAccessDeniedException("Notu yalnızca yazarı değiştirebilir ya da silebilir.");
        }

        await EnsureParentWritableAsync(note.ParentEntityKey, note.ParentId, cancellationToken);
        return note;
    }

    /// <summary>Bağlı kaydı (kapsam dahil) kayıt boru hattıyla okuyabilir mi?</summary>
    private async Task<bool> ParentReadableAsync(string parentKey, Guid parentId, CancellationToken cancellationToken) =>
        Registry.Find(parentKey) is not null
        && parentKey != EntityKey
        && (await Pipeline.GetAsync(new RecordRef(parentKey, parentId), cancellationToken)).IsSuccess;

    private async Task EnsureParentWritableAsync(string parentKey, Guid parentId, CancellationToken cancellationToken)
    {
        if (!await access.CanAsync(parentKey, RecordOperation.Update, cancellationToken))
        {
            throw new RecordAccessDeniedException("Bu kayda not eklemek ya da notu değiştirmek için kayıtta güncelleme izni gerekir.");
        }

        if (Registry.Find(parentKey) is IRecordWriteGuard guard && await guard.WriteDeniedAsync(parentId, cancellationToken) is { } denied)
        {
            throw new RecordAccessDeniedException(denied);
        }

        var moduleKey = parentKey[..parentKey.IndexOf('.', StringComparison.Ordinal)];
        if (tenantAccessor.Current is not { } tenant || !await license.IsWritableAsync(tenant.TenantId, moduleKey, cancellationToken))
        {
            throw new RecordAccessDeniedException("Modül lisansı salt okunur evrede; not eklenemez, değiştirilemez ya da silinemez.");
        }
    }

    private static (object? Value, string? Error) Parse(string name, JsonNode? node)
    {
        if (IsEmpty(node))
        {
            return (null, name is "parentEntityKey" or "parentId" or "body" ? "Bu alan zorunludur." : null);
        }

        if (node!.GetValueKind() != JsonValueKind.String)
        {
            return (null, "Metin bekleniyor.");
        }

        var text = node.GetValue<string>().Trim();
        return name switch
        {
            "parentEntityKey" => text.Length <= RecordNote.ParentEntityKeyMaxLength && text.Contains('.') ? (text, null) : (null, "Geçersiz kayıt türü."),
            "parentId" => Guid.TryParse(text, out var id) && id != Guid.Empty ? (id, null) : (null, "Geçersiz kimlik."),
            "body" => text.Length <= RecordNote.BodyMaxLength ? (text, null) : (null, $"En fazla {RecordNote.BodyMaxLength} karakter olabilir."),
            _ => (null, "Bilinmeyen alan."),
        };
    }

    private static bool IsEmpty(JsonNode? node) =>
        node is null || (node.GetValueKind() == JsonValueKind.String && string.IsNullOrWhiteSpace(node.GetValue<string>()));

    private static RecordValidationException Invalid(string field, string message) => new([new RecordFieldError(field, message)]);

    private RecordData ToData(RecordNote n) => new(EntityKey, n.Id, n.Version, new JsonObject
    {
        ["parentEntityKey"] = n.ParentEntityKey,
        ["parentId"] = n.ParentId.ToString(),
        ["body"] = n.Body,
        ["createdByUserId"] = n.CreatedByUserId?.ToString(),
        ["createdAt"] = n.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
    });

    private static void EnsureVersion(RecordNote entity, long? expectedVersion)
    {
        if (expectedVersion is { } expected && expected != entity.Version)
        {
            throw new RecordConflictException(
                $"Kayıt siz düzenlerken değişti (beklenen sürüm {expected}, güncel sürüm {entity.Version}). Güncel halini alıp tekrar deneyin.");
        }
    }
}
