using System.Net;
using System.Runtime.CompilerServices;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Modulix.Core.Files;

namespace Modulix.Infrastructure.Files;

/// <summary>
/// S3 uyumlu nesne deposu adaptörü (mimari değerlendirme A4). Nesne anahtarı yerel adaptörle aynı düzendedir:
/// <c>{önek}{tenantId:N}/{anahtar[0..2]}/{anahtar}</c> (<see cref="BlobKeys"/>); tenant dışa aktarma ve silme bu önekle listelenir.
/// Yazma koşulludur (<c>If-None-Match: *</c>): var olan anahtar ezilmez. S3'te tek PUT bütünseldir, yarım nesne görünmez.
/// Sağlama toplamı yalnızca gerektiğinde hesaplanır; AWS dışı uyumlu depolar (R2, Versity, Ceph) varsayılan bütünlük başlıklarını tanımayabilir.
/// </summary>
internal sealed class S3BlobStorage : IBlobStorage, IDisposable
{
    private const int DeleteBatchSize = 1000;

    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly string _prefix;

    public S3BlobStorage(IOptions<FileStorageOptions> options)
    {
        S3StorageOptions settings = options.Value.S3;
        if (string.IsNullOrWhiteSpace(settings.Bucket))
        {
            throw new InvalidOperationException("Storage:Provider S3 iken Storage:S3:Bucket zorunludur.");
        }

        _bucket = settings.Bucket;
        _prefix = string.IsNullOrWhiteSpace(settings.Prefix) ? string.Empty : settings.Prefix.TrimEnd('/') + "/";

        AmazonS3Config config = new()
        {
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (string.IsNullOrWhiteSpace(settings.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region ?? "eu-central-1");
            config.ForcePathStyle = settings.ForcePathStyle ?? false;
        }
        else
        {
            config.ServiceURL = settings.ServiceUrl;
            config.AuthenticationRegion = settings.Region ?? "us-east-1";
            config.ForcePathStyle = settings.ForcePathStyle ?? true;
        }

        _client = string.IsNullOrWhiteSpace(settings.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), config);
    }

    public async Task PutAsync(Guid tenantId, string key, Stream content, CancellationToken cancellationToken)
    {
        PutObjectRequest request = new()
        {
            BucketName = _bucket,
            Key = ObjectKey(tenantId, key),
            InputStream = content,
            AutoCloseStream = false,
            IfNoneMatch = "*",
        };
        try
        {
            await _client.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            throw new IOException("Bu depolama anahtarında içerik zaten var.", exception);
        }
    }

    public async Task<Stream?> OpenReadAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        try
        {
            GetObjectResponse response = await _client.GetObjectAsync(_bucket, ObjectKey(tenantId, key), cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        try
        {
            await _client.DeleteObjectAsync(_bucket, ObjectKey(tenantId, key), cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Silme zaten idempotenttir; bazı uyumlu depolar olmayan nesnede 404 döner.
        }
    }

    public async Task<int> ExportTenantAsync(Guid tenantId, string directory, CancellationToken cancellationToken)
    {
        int count = 0;
        await foreach (string objectKey in ListTenantKeysAsync(tenantId, cancellationToken))
        {
            string name = objectKey[(objectKey.LastIndexOf('/') + 1)..];
            if (!BlobKeys.IsValid(name))
            {
                continue;
            }

            Directory.CreateDirectory(directory);
            using GetObjectResponse response = await _client.GetObjectAsync(_bucket, objectKey, cancellationToken);
            await using (FileStream target = new(Path.Combine(directory, name), FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await response.ResponseStream.CopyToAsync(target, cancellationToken);
            }

            count++;
        }

        return count;
    }

    public async IAsyncEnumerable<StoredBlob> ListAsync(Guid tenantId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ListObjectsV2Request request = new() { BucketName = _bucket, Prefix = TenantPrefix(tenantId) };
        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request, cancellationToken);
            foreach (S3Object item in response.S3Objects ?? [])
            {
                string name = item.Key[(item.Key.LastIndexOf('/') + 1)..];
                if (BlobKeys.IsValid(name))
                {
                    yield return new StoredBlob(name, item.LastModified is { } modified
                        ? new DateTimeOffset(DateTime.SpecifyKind(modified, DateTimeKind.Utc))
                        : DateTimeOffset.UtcNow);
                }
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);
    }

    public async Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // Önce bütün anahtarlar toplanır: listeleme sürerken silmek sayfalamayı kaydırabilir.
        List<string> keys = [];
        await foreach (string objectKey in ListTenantKeysAsync(tenantId, cancellationToken))
        {
            keys.Add(objectKey);
        }

        foreach (string[] batch in keys.Chunk(DeleteBatchSize))
        {
            DeleteObjectsResponse response = await _client.DeleteObjectsAsync(
                new DeleteObjectsRequest
                {
                    BucketName = _bucket,
                    Objects = [.. batch.Select(k => new KeyVersion { Key = k })],
                    Quiet = true,
                },
                cancellationToken);
            if (response.DeleteErrors is { Count: > 0 } errors)
            {
                throw new IOException($"Tenant içeriği silinemedi: {errors.Count} nesne ({errors[0].Code}).");
            }
        }

        return keys.Count;
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        // En çok bir nesne listelemek kovaya erişimi ve kimliği birlikte doğrular.
        await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, Prefix = _prefix, MaxKeys = 1 }, cancellationToken);
    }

    public void Dispose() => _client.Dispose();

    private string TenantPrefix(Guid tenantId) => _prefix + BlobKeys.TenantSegment(tenantId) + "/";

    private string ObjectKey(Guid tenantId, string key)
    {
        (string shard, string name) = BlobKeys.Locate(key);
        return $"{TenantPrefix(tenantId)}{shard}/{name}";
    }

    private async IAsyncEnumerable<string> ListTenantKeysAsync(Guid tenantId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ListObjectsV2Request request = new() { BucketName = _bucket, Prefix = TenantPrefix(tenantId) };
        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request, cancellationToken);
            foreach (S3Object item in response.S3Objects ?? [])
            {
                yield return item.Key;
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);
    }
}
