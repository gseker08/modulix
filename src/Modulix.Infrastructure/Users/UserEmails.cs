using System.Net;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Notifications;

namespace Modulix.Infrastructure.Users;

/// <summary>
/// Kullanıcı yönetimi e-postaları. Metinler alıcının dilinde sözlükten gelir (<c>email.user.*</c>, ADR-0034 §6, dilim 18b); dil
/// e-postayla birlikte kuyruğa yazılır. Kullanıcı girdisi (ad, tenant adı) HTML'e kodlanarak eklenir.
/// </summary>
internal static class UserEmails
{
    public static EmailMessage Invitation(LocalizedTexts t, string to, string displayName, string tenantName, string invitedBy, string link) =>
        Build(
            t,
            to,
            displayName,
            t.Get("email.user.invitation.subject", tenantName),
            [t.Get("email.user.invitation.p1", invitedBy, tenantName), t.Get("email.user.invitation.p2")],
            (t.Get("email.user.invitation.action"), link),
            t.Get("email.user.invitation.footer"));

    public static EmailMessage AddedToTenant(LocalizedTexts t, string to, string displayName, string tenantName, string invitedBy, string link) =>
        Build(
            t,
            to,
            displayName,
            t.Get("email.user.added.subject", tenantName),
            [t.Get("email.user.added.p1", invitedBy, tenantName), t.Get("email.user.added.p2")],
            (t.Get("email.user.added.action"), link),
            t.Get("email.user.added.footer"));

    public static EmailMessage PasswordReset(LocalizedTexts t, string to, string displayName, string tenantName, string requestedBy, string link) =>
        Build(
            t,
            to,
            displayName,
            t.Get("email.user.password-reset.subject"),
            [t.Get("email.user.password-reset.p1", tenantName, requestedBy), t.Get("email.user.password-reset.p2")],
            (t.Get("email.user.password-reset.action"), link),
            t.Get("email.user.password-reset.footer"));

    /// <summary>Kişinin kendi istediği şifre sıfırlama bağlantısı (Y7a, "Şifremi unuttum").</summary>
    public static EmailMessage ForgotPassword(LocalizedTexts t, string to, string displayName, string link) =>
        Build(
            t,
            to,
            displayName,
            t.Get("email.user.password-reset.subject"),
            [t.Get("email.user.forgot-password.p1"), t.Get("email.user.password-reset.p2")],
            (t.Get("email.user.password-reset.action"), link),
            t.Get("email.user.forgot-password.footer"));

    public static EmailMessage ConfirmEmailChange(LocalizedTexts t, string to, string displayName, string link) =>
        Build(
            t,
            to,
            displayName,
            t.Get("email.user.confirm-email.subject"),
            [t.Get("email.user.confirm-email.p1"), t.Get("email.user.confirm-email.p2")],
            (t.Get("email.user.confirm-email.action"), link),
            t.Get("email.user.confirm-email.footer"));

    public static EmailMessage EmailChanged(LocalizedTexts t, string to, string displayName, string newEmail, string link) =>
        Build(
            t,
            to,
            displayName,
            t.Get("email.user.email-changed.subject"),
            [t.Get("email.user.email-changed.p1", newEmail), t.Get("email.user.email-changed.p2")],
            (t.Get("email.user.email-changed.action"), link),
            t.Get("email.common.informational"));

    public static EmailMessage RecoveryCodeUsed(LocalizedTexts t, string to, string displayName, int left, string link) =>
        Build(
            t,
            to,
            displayName,
            t.Get("email.user.recovery-code.subject"),
            [t.Get("email.user.recovery-code.p1", left), t.Get("email.user.recovery-code.p2")],
            (t.Get("email.user.recovery-code.action"), link),
            t.Get("email.user.recovery-code.footer"));

    /// <summary>
    /// Kayıt devri (ADR-0004, ADR-0007): devirde kayıt başına atama e-postası gitmez; yeni sorumluya tek özet gider.
    /// <paramref name="link"/> yoksa (App:PublicUrl ayarlı değil) bağlantı konmaz.
    /// </summary>
    public static EmailMessage RecordsTransferred(
        LocalizedTexts t, string to, string displayName, string transferredBy, string previousOwner, string summary, string? link) =>
        Build(
            t,
            to,
            displayName,
            t.Get("email.user.records-transferred.subject"),
            [t.Get("email.user.records-transferred.p1", transferredBy, previousOwner, summary), t.Get("email.user.records-transferred.p2")],
            link is null ? null : (t.Get("email.common.open-app"), link),
            t.Get("email.common.informational"));

    private static EmailMessage Build(
        LocalizedTexts t, string to, string displayName, string subject, string[] paragraphs, (string Text, string Url)? action, string footer)
    {
        var greeting = t.Get("email.common.greeting", displayName);
        var html = string.Concat(
            $"<p>{Encode(greeting)}</p>",
            string.Concat(paragraphs.Select(p => $"<p>{Encode(p)}</p>")),
            action is { } a ? $"<p><a href=\"{Encode(a.Url)}\">{Encode(a.Text)}</a></p>" : string.Empty,
            $"<p style=\"color:#666\">{Encode(footer)}</p>");

        var text = string.Join(
            "\n\n",
            [greeting, .. paragraphs, .. action is { } b ? [$"{b.Text}: {b.Url}"] : Array.Empty<string>(), footer]);

        return new EmailMessage(to, subject, html) { ToName = displayName, TextBody = text, Language = t.Language };
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
