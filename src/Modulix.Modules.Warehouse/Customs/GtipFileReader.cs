using Modulix.Core.Contracts.Files;
using Modulix.Modules.Warehouse.Contracts.Customs;

namespace Modulix.Modules.Warehouse.Customs;

/// <summary>
/// GTİP dosyası okuyucu (ADR-0032 K9): <c>.xlsx</c> (ilk sayfa) ve <c>.csv</c>/<c>.txt</c>; biçimi ortak <see cref="ITabularFileReader"/> hücre listesine indirir,
/// yorumlama <see cref="GtipImportRules"/>'tadır. Okunamayan ya da beklenmedik dosya hata fırlatmaz, <see cref="GtipFileReadResult.FatalError"/> döner.
/// </summary>
internal sealed class GtipFileReader(ITabularFileReader files) : IGtipFileReader
{
    public GtipFileReadResult Read(byte[] content, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            return GtipFileReadResult.Fatal("Dosya boş.");
        }

        if (content.Length > GtipImportRules.MaxFileBytes)
        {
            return GtipFileReadResult.Fatal($"Dosya en fazla {GtipImportRules.MaxFileBytes / (1024 * 1024)} MB olabilir.");
        }

        try
        {
            return GtipImportRules.Interpret(files.ReadRows(content, fileName));
        }
        catch (TabularFileException ex)
        {
            return GtipFileReadResult.Fatal(ex.Message);
        }
    }
}
