using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Hr.Domain;

namespace Modulix.Modules.Hr.Records;

/// <summary>
/// Blog yazıları (ADR-0042 dilim 8, bölüm 27). "Blog yönetimi" izni olan bütün yazıları (taslak dahil) görür, yazar, yayınlar; "blog yazma" izni olan
/// çalışan kendi yazısını yazar, ek ekler ve onaya gönderir (<see cref="IBlogReview"/>), yayınlayamaz. Yazar kendi yazısını yalnızca taslakken ya da
/// geri çevrilmişken değiştirir ve siler; onay bekleyen ve yayındaki yazı kilitlidir. Diğer okur yalnızca yayındaki yazıları ve kendi yazılarını görür,
/// görmediği yazı "bulunamadı"dır (yazıya bağlı dosyalar, yorumlar ve beğeniler de). İlk yayında yayın anı yazılır. Silinen yazının yorum ve
/// beğenileri de silinir (bölüm 23d).
/// </summary>
internal sealed class BlogPostRecordStore(HrStoreContext context) : HrRecordStore<BlogPost>(context)
{
    private const string LockedMessage = "Onay bekleyen ya da yayındaki yazı değiştirilemez; yazı geri çevrilirse düzeltilir.";

    private readonly ContentStats stats = new(context);

    public override string EntityKey => HrModule.Entities.BlogPost;

    protected override string ReadPermission => HrModule.Permissions.BlogRead;

    /// <summary>Yazma izni "blog yazma"dır; yönetim (başkasının yazısı, yayın) depoda ayrıca denetlenir. Yöneten rol iki izni birlikte taşır.</summary>
    protected override string WritePermission => HrModule.Permissions.BlogWrite;

    protected override IReadOnlyList<RecordField<BlogPost>> Fields => fields ??=
    [
        Field.Text<BlogPost>("title", BlogPost.TitleMaxLength, p => p.Title, (p, v) => p.Title = v!, required: true),
        Field.Text<BlogPost>("summary", BlogPost.SummaryMaxLength, p => p.Summary, (p, v) => p.Summary = v),
        Field.Text<BlogPost>("body", BlogPost.BodyMaxLength, p => p.Body, (p, v) => p.Body = v!, required: true),
        Field.Flag<BlogPost>("isPublished", p => p.IsPublished, (p, v) => p.IsPublished = v),
        Field.Computed<BlogPost>("publishedAt", p => p.PublishedAt?.ToString("O")),
        Field.Computed<BlogPost>("authorUserId", p => p.AuthorUserId?.ToString()),
        Field.Computed<BlogPost>("status", p => p.Status),
        Field.Computed<BlogPost>("reviewNote", p => p.ReviewNote),
        Field.Computed<BlogPost>("submittedAt", p => p.SubmittedAt?.ToString("O")),
        .. stats.Fields<BlogPost>(),
    ];

    private IReadOnlyList<RecordField<BlogPost>>? fields;

    protected override IReadOnlyDictionary<string, RecordFilter<BlogPost>> Filters { get; } = new Dictionary<string, RecordFilter<BlogPost>>
    {
        ["isPublished"] = (q, v) => bool.TryParse(v, out var published) ? q.Where(p => p.IsPublished == published) : null,
        ["status"] = (q, v) => v switch
        {
            BlogPost.Statuses.Published => q.Where(p => p.IsPublished),
            BlogPost.Statuses.Draft => q.Where(p => !p.IsPublished && p.ReviewStatus == null),
            BlogPost.Statuses.Submitted or BlogPost.Statuses.Returned => q.Where(p => !p.IsPublished && p.ReviewStatus == v),
            _ => null,
        },
        ["authorUserId"] = (q, v) => FilterValue.Id(v) is { } id ? q.Where(p => p.AuthorUserId == id) : null,
    };

    protected override IOrderedQueryable<BlogPost> Order(IQueryable<BlogPost> source) =>
        source.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.CreatedAt);

    protected override IReadOnlyCollection<string>? SortKeys { get; } = WithDescending("publishedAt", "submittedAt", "title", "createdAt");

    protected override IOrderedQueryable<BlogPost> Order(IQueryable<BlogPost> source, string? sort) => sort?.TrimStart('-') switch
    {
        "publishedAt" => By(source, p => p.PublishedAt, sort.StartsWith('-')).ThenByDescending(p => p.CreatedAt),
        "submittedAt" => By(source, p => p.SubmittedAt, sort.StartsWith('-')).ThenByDescending(p => p.CreatedAt),
        "title" => By(source, p => p.Title, sort.StartsWith('-')).ThenByDescending(p => p.CreatedAt),
        "createdAt" => By(source, p => p.CreatedAt, sort.StartsWith('-')),
        _ => Order(source),
    };

    protected override IQueryable<BlogPost> Search(IQueryable<BlogPost> source, string term) =>
        source.Where(p => p.Title.Contains(term) || (p.Summary != null && p.Summary.Contains(term)) || p.Body.Contains(term));

    protected override Task<IQueryable<BlogPost>> RestrictAsync(IQueryable<BlogPost> source, CancellationToken cancellationToken) =>
        ForReaderAsync(Context, source, cancellationToken);

    /// <summary>Kullanıcının görebildiği yazılar: yöneten hepsini, diğerleri yayındakileri ve kendi yazılarını (yorum ve beğeni de bu kuralı kullanır; bölüm 23d).</summary>
    internal static async Task<IQueryable<BlogPost>> ForReaderAsync(HrStoreContext context, IQueryable<BlogPost> source, CancellationToken cancellationToken)
    {
        if (await context.Access.HasPermissionAsync(HrModule.Permissions.BlogManage, cancellationToken))
        {
            return source;
        }

        var me = context.CurrentUserId ?? Guid.Empty;
        return source.Where(p => p.IsPublished || p.AuthorUserId == me);
    }

    protected override Task PrepareAsync(IReadOnlyCollection<BlogPost> entities, CancellationToken cancellationToken) =>
        stats.LoadAsync(entities.Select(p => p.Id), cancellationToken);

    protected override async Task BeforeDeleteAsync(BlogPost entity, CancellationToken cancellationToken)
    {
        if (!await ManagesAsync(cancellationToken))
        {
            EnsureAuthorMayChange(entity.AuthorUserId, entity.IsPublished, entity.ReviewStatus);
        }

        await ContentStats.RemoveAsync(Context, entity.Id, cancellationToken);
    }

    /// <summary>Ek yalnızca yazıyı değiştirebilen tarafından eklenir ve silinir (ADR-0040 K2): yöneten her zaman, yazar taslakta ve geri çevrilmişte.</summary>
    protected override async Task<string?> FileWriteDeniedAsync(BlogPost entity, CancellationToken cancellationToken)
    {
        if (await ManagesAsync(cancellationToken))
        {
            return null;
        }

        if (entity.AuthorUserId != Context.CurrentUserId)
        {
            return "Yalnızca kendi yazınızın eklerini değiştirebilirsiniz.";
        }

        return entity.IsPublished || entity.ReviewStatus == BlogPost.Statuses.Submitted ? LockedMessage : null;
    }

    protected override async Task AppliedAsync(BlogPost entity, JsonObject values, bool isNew, CancellationToken cancellationToken)
    {
        if (!isNew && Context.Gate.Status is { } decision)
        {
            ApplyReview(entity, decision);
            return;
        }

        var manages = await ManagesAsync(cancellationToken);
        if (isNew)
        {
            entity.AuthorUserId = Context.CurrentUserId;
        }
        else if (!manages)
        {
            var stored = await Context.Data.Set<BlogPost>().AsNoTracking().Where(p => p.Id == entity.Id)
                .Select(p => new { p.AuthorUserId, p.IsPublished, p.ReviewStatus }).SingleAsync(cancellationToken);
            EnsureAuthorMayChange(stored.AuthorUserId, stored.IsPublished, stored.ReviewStatus);
        }

        if (!manages && entity.IsPublished)
        {
            throw new RecordValidationException([new RecordFieldError("isPublished", "Yazıyı yayınlama yetkiniz yok; yazıyı onaya gönderin.")]);
        }

        if (entity.IsPublished)
        {
            // Yönetenin doğrudan yayını onay yerine geçer.
            entity.PublishedAt ??= DateTimeOffset.UtcNow;
            entity.ReviewStatus = null;
            entity.ReviewNote = null;
        }
    }

    private void ApplyReview(BlogPost entity, string decision)
    {
        switch (decision)
        {
            case BlogPost.Statuses.Submitted:
                entity.ReviewStatus = BlogPost.Statuses.Submitted;
                entity.ReviewNote = null;
                entity.SubmittedAt = DateTimeOffset.UtcNow;
                break;
            case BlogPost.Statuses.Returned:
                entity.ReviewStatus = BlogPost.Statuses.Returned;
                entity.ReviewNote = Context.Gate.Reason;
                break;
            case BlogPost.Statuses.Published:
                entity.IsPublished = true;
                entity.PublishedAt ??= DateTimeOffset.UtcNow;
                entity.ReviewStatus = null;
                entity.ReviewNote = null;
                break;
        }
    }

    private Task<bool> ManagesAsync(CancellationToken cancellationToken) => Context.Access.HasPermissionAsync(HrModule.Permissions.BlogManage, cancellationToken);

    private void EnsureAuthorMayChange(Guid? authorUserId, bool isPublished, string? reviewStatus)
    {
        if (authorUserId != Context.CurrentUserId)
        {
            throw new RecordAccessDeniedException("Yalnızca kendi yazınızı değiştirebilir ve silebilirsiniz.");
        }

        if (isPublished || reviewStatus == BlogPost.Statuses.Submitted)
        {
            throw new RecordConflictException(LockedMessage);
        }
    }
}

/// <summary>
/// Fotoğraf albümleri (ADR-0042 dilim 8). Fotoğraflar albüme bağlı ADR-0040 dosyalarıdır: okur yalnızca yayındaki albümü ve fotoğraflarını görür,
/// fotoğrafı yalnızca "galeri yönetimi" izni olan ekler ve siler (dosya yükleme kayıt türünün güncelleme iznini ister).
/// </summary>
internal sealed class PhotoAlbumRecordStore(HrStoreContext context) : HrRecordStore<PhotoAlbum>(context)
{
    public override string EntityKey => HrModule.Entities.PhotoAlbum;

    protected override string ReadPermission => HrModule.Permissions.GalleryRead;

    protected override string WritePermission => HrModule.Permissions.GalleryManage;

    protected override IReadOnlyList<RecordField<PhotoAlbum>> Fields { get; } =
    [
        Field.Text<PhotoAlbum>("title", PhotoAlbum.TitleMaxLength, a => a.Title, (a, v) => a.Title = v!, required: true),
        Field.Text<PhotoAlbum>("description", PhotoAlbum.DescriptionMaxLength, a => a.Description, (a, v) => a.Description = v),
        Field.Date<PhotoAlbum>("eventDate", a => a.EventDate, (a, v) => a.EventDate = v),
        Field.Flag<PhotoAlbum>("isPublished", a => a.IsPublished, (a, v) => a.IsPublished = v),
        Field.Computed<PhotoAlbum>("publishedAt", a => a.PublishedAt?.ToString("O")),
        Field.Computed<PhotoAlbum>("authorUserId", a => a.AuthorUserId?.ToString()),
    ];

    protected override IReadOnlyDictionary<string, RecordFilter<PhotoAlbum>> Filters { get; } = new Dictionary<string, RecordFilter<PhotoAlbum>>
    {
        ["isPublished"] = (q, v) => bool.TryParse(v, out var published) ? q.Where(a => a.IsPublished == published) : null,
    };

    protected override IOrderedQueryable<PhotoAlbum> Order(IQueryable<PhotoAlbum> source) =>
        source.OrderByDescending(a => a.EventDate).ThenByDescending(a => a.CreatedAt);

    protected override IReadOnlyCollection<string>? SortKeys { get; } = WithDescending("eventDate", "title", "createdAt");

    protected override IOrderedQueryable<PhotoAlbum> Order(IQueryable<PhotoAlbum> source, string? sort) => sort?.TrimStart('-') switch
    {
        "eventDate" => By(source, a => a.EventDate, sort.StartsWith('-')).ThenByDescending(a => a.CreatedAt),
        "title" => By(source, a => a.Title, sort.StartsWith('-')).ThenByDescending(a => a.CreatedAt),
        "createdAt" => By(source, a => a.CreatedAt, sort.StartsWith('-')),
        _ => Order(source),
    };

    protected override IQueryable<PhotoAlbum> Search(IQueryable<PhotoAlbum> source, string term) =>
        source.Where(a => a.Title.Contains(term) || (a.Description != null && a.Description.Contains(term)));

    protected override async Task<IQueryable<PhotoAlbum>> RestrictAsync(IQueryable<PhotoAlbum> source, CancellationToken cancellationToken) =>
        await Context.Access.HasPermissionAsync(HrModule.Permissions.GalleryManage, cancellationToken) ? source : source.Where(a => a.IsPublished);

    protected override Task AppliedAsync(PhotoAlbum entity, JsonObject values, bool isNew, CancellationToken cancellationToken)
    {
        if (isNew)
        {
            entity.AuthorUserId = Context.CurrentUserId;
        }

        if (entity.IsPublished)
        {
            entity.PublishedAt ??= DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }
}
