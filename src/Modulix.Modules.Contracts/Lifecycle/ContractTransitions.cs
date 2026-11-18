namespace Modulix.Modules.Contracts.Lifecycle;

/// <summary>Eylem anahtarları; API yolları ve arayüz etiketleri bunlarla eşleşir.</summary>
public static class ContractAction
{
    public const string SendToReview = "send-to-review";
    public const string MarkReady = "mark-ready";
    public const string ReopenDraft = "reopen-draft";
    public const string SubmitForApproval = "submit-for-approval";
    public const string WithdrawApproval = "withdraw-approval";
    public const string Approve = "approve";
    public const string RejectApproval = "reject-approval";
    public const string ReturnForChanges = "return-for-changes";
    public const string Cancel = "cancel";
    public const string Revise = "revise";

    public static IReadOnlyList<string> All { get; } =
        [SendToReview, MarkReady, ReopenDraft, SubmitForApproval, WithdrawApproval, Approve, RejectApproval, ReturnForChanges, Cancel, Revise];
}

/// <summary>Geçiş kararı için sözleşmenin ilgili durumu (veritabanı bilmez).</summary>
public sealed record ContractState(
    string Preparation,
    string Approval,
    string Signature,
    string Effective,
    bool HasStartDate,
    bool HasEndOrIndefinite,
    bool HasParty = true);

/// <summary>
/// Durum geçişi ön koşulları (ADR-0022, dilim 2). Her eylem için tetikleyici kullanıcıdır; yetki iş akışı servisinde, ön koşul burada,
/// yan etkiler (sürüm dondurma, tarihçe) serviste tanımlıdır. Kurallar API seviyesinde uygulanır; arayüz yalnızca aynı sonucu gösterir.
/// </summary>
public static class ContractTransitions
{
    public static ContractRuleResult Can(string action, ContractState s)
    {
        var signing = s.Signature is SignatureStatus.Pending or SignatureStatus.PartiallySigned;
        var signed = s.Signature == SignatureStatus.Completed;
        var closed = s.Effective is EffectiveStatus.Expired or EffectiveStatus.Terminated or EffectiveStatus.Cancelled;

        switch (action)
        {
            case ContractAction.SendToReview:
                if (s.Preparation != PreparationStatus.Draft)
                {
                    return ContractRuleResult.Deny("not-draft", ContractsMessages.Transition.ReviewDraftOnly);
                }

                return closed ? Deny("closed") : ContractRuleResult.Ok;

            case ContractAction.MarkReady:
                if (s.Preparation != PreparationStatus.InReview)
                {
                    return ContractRuleResult.Deny("not-in-review", ContractsMessages.Transition.ReadyInReviewOnly);
                }

                return MissingBasics(s) ?? ContractRuleResult.Ok;

            case ContractAction.ReopenDraft:
                if (s.Preparation == PreparationStatus.Draft)
                {
                    return ContractRuleResult.Deny("already-draft", ContractsMessages.Transition.AlreadyDraft);
                }

                if (s.Approval == ApprovalStatus.Pending)
                {
                    return ContractRuleResult.Deny("approval-pending", ContractsMessages.Transition.DraftApprovalPending);
                }

                if (signing || signed)
                {
                    return ContractRuleResult.Deny("signature-in-progress", ContractsMessages.Transition.DraftSignature);
                }

                return closed ? Deny("closed") : ContractRuleResult.Ok;

            case ContractAction.SubmitForApproval:
                if (s.Preparation != PreparationStatus.ReadyToPublish)
                {
                    return ContractRuleResult.Deny("not-ready", ContractsMessages.Transition.SubmitNotReady);
                }

                if (s.Approval == ApprovalStatus.Pending)
                {
                    return ContractRuleResult.Deny("approval-pending", ContractsMessages.Transition.AlreadyPending);
                }

                if (s.Approval == ApprovalStatus.Approved)
                {
                    return ContractRuleResult.Deny("already-approved", ContractsMessages.Transition.AlreadyApproved);
                }

                if (signing || signed)
                {
                    return ContractRuleResult.Deny("signature-in-progress", ContractsMessages.Transition.SubmitSignature);
                }

                return MissingBasics(s) ?? (closed ? Deny("closed") : ContractRuleResult.Ok);

            case ContractAction.WithdrawApproval:
                return s.Approval == ApprovalStatus.Pending ? ContractRuleResult.Ok : ContractRuleResult.Deny("not-pending", ContractsMessages.Transition.NoPendingApproval);

            case ContractAction.Approve:
            case ContractAction.RejectApproval:
            case ContractAction.ReturnForChanges:
                return s.Approval == ApprovalStatus.Pending ? ContractRuleResult.Ok : ContractRuleResult.Deny("not-pending", ContractsMessages.Transition.NoPendingVersion);

            case ContractAction.Cancel:
                if (s.Effective != EffectiveStatus.NotInForce)
                {
                    return ContractRuleResult.Deny("in-force", ContractsMessages.Transition.CancelInForce);
                }

                if (signed || s.Signature == SignatureStatus.PartiallySigned)
                {
                    return ContractRuleResult.Deny("signed", ContractsMessages.Transition.CancelSigned);
                }

                return ContractRuleResult.Ok;

            case ContractAction.Revise:
                if (s.Approval == ApprovalStatus.Pending || signing)
                {
                    return ContractRuleResult.Deny("in-progress", ContractsMessages.Transition.ReviseInProgress);
                }

                if (!(signed || EffectiveStatus.IsBinding(s.Effective) || s.Effective == EffectiveStatus.Expired))
                {
                    return ContractRuleResult.Deny("not-signed", ContractsMessages.Transition.ReviseNotSigned);
                }

                return s.Preparation == PreparationStatus.Draft && !signed
                    ? ContractRuleResult.Deny("draft-open", ContractsMessages.Transition.DraftRevisionOpen)
                    : ContractRuleResult.Ok;

            default:
                return ContractRuleResult.Deny("unknown-action", ContractsMessages.Transition.UnknownAction);
        }
    }

    /// <summary>Eylem gerekçe ister mi? (Ret, iptal ve revizyon gerekçesiz yapılmaz.)</summary>
    public static bool RequiresReason(string action) => action is ContractAction.RejectApproval or ContractAction.ReturnForChanges or ContractAction.Cancel or ContractAction.Revise;

    private static ContractRuleResult? MissingBasics(ContractState s)
    {
        if (!s.HasParty)
        {
            return ContractRuleResult.Deny("missing-party", ContractsMessages.Transition.MissingParty);
        }

        if (!s.HasStartDate)
        {
            return ContractRuleResult.Deny("missing-start", ContractsMessages.Transition.MissingStart);
        }

        return s.HasEndOrIndefinite ? null : ContractRuleResult.Deny("missing-end", ContractsMessages.Transition.MissingEnd);
    }

    private static ContractRuleResult Deny(string code) => ContractRuleResult.Deny(code, ContractsMessages.Transition.Closed);
}
