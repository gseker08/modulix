using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;
using Modulix.Modules.Hr.Domain;

namespace Modulix.Modules.Hr.Records;

/// <summary>
/// Blog yazısının onay akışı (ADR-0042 bölüm 27): yazar taslağını ya da geri çevrilen yazısını onaya gönderir ("blog yazma"); yöneten onay bekleyen
/// yazıyı yayınlar ya da gerekçeyle geri çevirir ("blog yönetimi"). Durum yalnızca bu komutlarla değişir; kayıt boru hattından geçer (denetim, olay).
/// </summary>
public interface IBlogReview
{
    Task<RecordResult<IReadOnlyList<LeaveActionState>>> ActionsAsync(Guid postId, CancellationToken cancellationToken);

    Task<RecordResult<RecordData>> RunAsync(Guid postId, string action, long version, string? reason, CancellationToken cancellationToken);
}

internal sealed class BlogReview(IRecordPipeline pipeline, HrStoreContext context) : IBlogReview
{
    public static class Actions
    {
        public const string Submit = "submit";
        public const string Publish = "publish";
        public const string Return = "return";
    }

    public async Task<RecordResult<IReadOnlyList<LeaveActionState>>> ActionsAsync(Guid postId, CancellationToken cancellationToken)
    {
        var record = await pipeline.GetAsync(new RecordRef(HrModule.Entities.BlogPost, postId), cancellationToken);
        if (!record.IsSuccess)
        {
            return RecordResult<IReadOnlyList<LeaveActionState>>.Fail(record.Error!.Code, record.Error.Message, record.Error.FieldErrors);
        }

        var state = await StateAsync(postId, cancellationToken);
        List<LeaveActionState> actions = [];
        if (state.MaySubmit)
        {
            actions.Add(new LeaveActionState(Actions.Submit, "Onaya gönder", state.Status is BlogPost.Statuses.Draft or BlogPost.Statuses.Returned,
                state.Status is BlogPost.Statuses.Draft or BlogPost.Statuses.Returned ? null : HrMessages.Blog.SubmitState.Text,
                RequiresReason: false, Destructive: false));
        }

        if (state.Manages)
        {
            bool submitted = state.Status == BlogPost.Statuses.Submitted;
            string? message = submitted ? null : HrMessages.Blog.DecideState.Text;
            actions.Add(new LeaveActionState(Actions.Publish, "Onayla ve yayınla", submitted, message, RequiresReason: false, Destructive: false));
            actions.Add(new LeaveActionState(Actions.Return, "Geri çevir", submitted, message, RequiresReason: true, Destructive: true));
        }

        return RecordResult<IReadOnlyList<LeaveActionState>>.Ok(actions);
    }

    public async Task<RecordResult<RecordData>> RunAsync(Guid postId, string action, long version, string? reason, CancellationToken cancellationToken)
    {
        if (action is not (Actions.Submit or Actions.Publish or Actions.Return))
        {
            return RecordResult<RecordData>.Fail(RecordErrorCode.ValidationFailed, "Bilinmeyen eylem.", [new RecordFieldError("action", "Bilinmeyen eylem.")]);
        }

        var record = await pipeline.GetAsync(new RecordRef(HrModule.Entities.BlogPost, postId), cancellationToken);
        if (!record.IsSuccess)
        {
            return record;
        }

        var state = await StateAsync(postId, cancellationToken);
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        string permission;
        string to;
        if (action == Actions.Submit)
        {
            if (!state.MaySubmit)
            {
                return RecordResult<RecordData>.Fail(RecordErrorCode.PermissionDenied, HrMessages.Blog.SubmitOwnOnly);
            }

            if (state.Status is not (BlogPost.Statuses.Draft or BlogPost.Statuses.Returned))
            {
                return RecordResult<RecordData>.Fail(RecordErrorCode.Conflict, HrMessages.Blog.SubmitState);
            }

            (permission, to) = (HrModule.Permissions.BlogWrite, BlogPost.Statuses.Submitted);
        }
        else
        {
            if (!state.Manages)
            {
                return RecordResult<RecordData>.Fail(RecordErrorCode.PermissionDenied, HrMessages.Workflow.NotAllowed);
            }

            if (state.Status != BlogPost.Statuses.Submitted)
            {
                return RecordResult<RecordData>.Fail(RecordErrorCode.Conflict, HrMessages.Blog.DecideState);
            }

            if (action == Actions.Return && (reason is null || reason.Length > BlogPost.ReviewNoteMaxLength))
            {
                var message = reason is null ? HrMessages.Blog.ReturnReasonRequired : HrMessages.Blog.ReviewNoteMax(BlogPost.ReviewNoteMaxLength);
                return RecordResult<RecordData>.Fail(RecordErrorCode.ValidationFailed, message, [new RecordFieldError("reason", message)]);
            }

            (permission, to) = (HrModule.Permissions.BlogManage, action == Actions.Publish ? BlogPost.Statuses.Published : BlogPost.Statuses.Returned);
        }

        using (context.Gate.Open(permission, to, action == Actions.Return ? reason : null))
        {
            return await pipeline.UpdateAsync(new RecordCommand(HrModule.Entities.BlogPost, postId, new JsonObject(), version), cancellationToken);
        }
    }

    private async Task<(string Status, bool MaySubmit, bool Manages)> StateAsync(Guid postId, CancellationToken cancellationToken)
    {
        var post = await context.Data.Set<BlogPost>().AsNoTracking().Where(p => p.Id == postId)
            .Select(p => new { p.IsPublished, p.ReviewStatus, p.AuthorUserId }).SingleAsync(cancellationToken);
        var status = post.IsPublished ? BlogPost.Statuses.Published : post.ReviewStatus ?? BlogPost.Statuses.Draft;
        var maySubmit = post.AuthorUserId is { } author && author == context.CurrentUserId
            && await context.Access.HasPermissionAsync(HrModule.Permissions.BlogWrite, cancellationToken);
        return (status, maySubmit, await context.Access.HasPermissionAsync(HrModule.Permissions.BlogManage, cancellationToken));
    }
}
