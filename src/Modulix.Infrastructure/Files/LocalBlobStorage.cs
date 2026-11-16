using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Modulix.Core.Files;

namespace Modulix.Infrastructure.Files;

/// <summary>
/// Yerel disk adaptörü (ADR-0040 K4). Yol: <c>{kök}/{tenantId:N}/{anahtar[0..2]}/{anahtar}</c>; anahtar biçimi <see cref="BlobKeys"/> ile denetlenir.
/// Yazma önce geçici dosyaya yapılır, sonra yerine taşınır: yarım dosya görünmez ve var olan anahtar ezilmez.
/// Yalnızca tek düğümlü dağıtım içindir; çok düğümde <see cref="S3BlobStorage"/> kullanılır.
/// </summary>
internal sealed class LocalBlobStorage(IOptions<FileStorageOptions> options) : IBlobStorage
{
    private const string TempMarker = ".part-";

    private readonly string _root = Path.GetFullPath(
        string.IsNullOrWhiteSpace(options.Value.LocalRoot)
            ? Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "blobs")
            : options.Value.LocalRoot);

    public async Task PutAsync(Guid tenantId, string key, Stream content, CancellationToken cancellationToken)
    {
        var path = PathFor(tenantId, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + TempMarker + Guid.NewGuid().ToString("N");
        try
        {
            await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(target, cancellationToken);
            }

            File.Move(temp, path, overwrite: false);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var path = PathFor(tenantId, key);
        return Task.FromResult<Stream?>(File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null);
    }

    public Task DeleteAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        TryDelete(PathFor(tenantId, key));
        return Task.CompletedTask;
    }

    public Task<int> ExportTenantAsync(Guid tenantId, string directory, CancellationToken cancellationToken)
    {
        var source = TenantDirectory(tenantId);
        if (!Directory.Exists(source))
        {
            return Task.FromResult(0);
        }

        Directory.CreateDirectory(directory);
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (name.Contains(TempMarker, StringComparison.Ordinal))
            {
                continue;
            }

            File.Copy(file, Path.Combine(directory, name), overwrite: true);
            count++;
        }

        return Task.FromResult(count);
    }

    public async IAsyncEnumerable<StoredBlob> ListAsync(Guid tenantId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var source = TenantDirectory(tenantId);
        if (!Directory.Exists(source))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (name.Contains(TempMarker, StringComparison.Ordinal) || !BlobKeys.IsValid(name))
            {
                continue;
            }

            yield return new StoredBlob(name, new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero));
        }

        await Task.CompletedTask;
    }

    public Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var source = TenantDirectory(tenantId);
        if (!Directory.Exists(source))
        {
            return Task.FromResult(0);
        }

        var count = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Count();
        Directory.Delete(source, recursive: true);
        return Task.FromResult(count);
    }

    public Task CheckAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    private string TenantDirectory(Guid tenantId) => Path.Combine(_root, BlobKeys.TenantSegment(tenantId));

    private string PathFor(Guid tenantId, string key)
    {
        (string shard, string name) = BlobKeys.Locate(key);
        return Path.Combine(TenantDirectory(tenantId), shard, name);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Silinemeyen geçici dosya sonraki temizlikte kalkar; asıl hata yutulmaz.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
