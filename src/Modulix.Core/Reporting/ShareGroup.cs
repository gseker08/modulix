namespace Modulix.Core.Reporting;

/// <summary>
/// Rapor ve pano paylaşımının hedef türü (ADR-0054 dilim 4a). <see cref="User"/> tek kullanıcıdır; diğerleri bir grubu hedefler ve üyelik
/// değiştikçe erişim de değişir (görünürlük her okumada yeniden hesaplanır).
/// </summary>
public enum ShareTargetKind
{
    User = 0,
    Role = 1,
    Team = 2,

    /// <summary>İş birimi (departman); alt birimlerin üyeleri de dahildir.</summary>
    BusinessUnit = 3,
}

/// <summary>Kullanıcı dışı paylaşım hedefi: bir rol, ekip ya da iş birimi.</summary>
public sealed record ShareGroup(ShareTargetKind Kind, Guid Id);

/// <summary>Paylaşım panelinin seçebileceği grup (ad ile).</summary>
public sealed record ShareGroupOption(ShareTargetKind Kind, Guid Id, string Name);
