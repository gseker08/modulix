using Modulix.Core.Contracts.Persistence;
using Modulix.Modules.Warehouse.Contracts.Customs;

namespace Modulix.Modules.Warehouse.Customs;

/// <summary>
/// GTİP kataloğunun bir sürümü (ADR-0032 K9): Bakanlık tarife cetvelinin <see cref="EffectiveFrom"/> tarihinden itibaren geçerli kopyası.
/// Platform geneli kayıttır (RLS dışı, <c>TenantId</c> yok). Sürüm değiştirilmez; düzeltme için silinip yeniden yüklenir. Kayıtlar
/// koda göre başvurduğundan sürüm silmek tenant verisini bozmaz.
/// </summary>
public sealed class GtipCatalogVersion : IPlatformReferenceEntity
{
    private GtipCatalogVersion()
    {
        // EF Core için
    }

    public GtipCatalogVersion(string name, DateOnly effectiveFrom, string? sourceFileName, int entryCount, Guid createdByUserId, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > GtipCatalogRules.NameMaxLength)
        {
            throw new ArgumentException($"Sürüm adı boş olamaz ve en çok {GtipCatalogRules.NameMaxLength} karakter olabilir.", nameof(name));
        }

        if (entryCount <= 0)
        {
            throw new ArgumentException("Sürümde en az bir GTİP kaydı olmalıdır.", nameof(entryCount));
        }

        Id = Guid.CreateVersion7();
        Name = name.Trim();
        EffectiveFrom = effectiveFrom;
        SourceFileName = string.IsNullOrWhiteSpace(sourceFileName) ? null : Truncate(sourceFileName.Trim(), GtipCatalogRules.FileNameMaxLength);
        EntryCount = entryCount;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Sürümün yürürlüğe girdiği gün; bir tarihte yürürlükteki sürüm, bu günü geçmemiş en yeni olandır.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    public string? SourceFileName { get; private set; }

    public int EntryCount { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Bir sürümdeki tarife satırı. Anahtar (sürüm, kod): bir kod sürümde bir kez geçer.</summary>
public sealed class GtipCatalogEntry : IPlatformReferenceEntity
{
    private GtipCatalogEntry()
    {
        // EF Core için
    }

    public GtipCatalogEntry(Guid versionId, GtipCatalogEntryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        VersionId = versionId;
        Code = input.Code;
        Description = input.Description;
        Unit = input.Unit;
    }

    public Guid VersionId { get; private set; }

    /// <summary>Ayraçsız rakamlar (<see cref="GtipRules"/>); metin saklanır, baştaki sıfır kaybolmaz.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Tarife cetvelindeki ölçü birimi (ör. "kg", "adet"); cetvelde yoksa boş.</summary>
    public string? Unit { get; private set; }
}
