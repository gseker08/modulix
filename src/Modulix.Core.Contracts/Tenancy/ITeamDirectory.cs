namespace Modulix.Core.Contracts.Tenancy;

/// <summary>Firmanın ekibi (çalışma grubu; departman değildir, ADR-0026).</summary>
public sealed record TeamInfo(Guid Id, string Name);

/// <summary>
/// Aktif tenant'ın ekipleri, modüllere salt okunur. Rapor kırılımı (ADR-0054 dilim 4b) bir kullanıcıyı tek ekibe bağlar: kullanıcı birden çok
/// ekipteyse <b>en önce katıldığı</b> ekip birincil sayılır, böylece kayıt hiçbir kırılımda iki kez sayılmaz. Ekip adları erişim yetkisi değildir.
/// </summary>
public interface ITeamDirectory
{
    /// <summary>Firmanın bütün ekipleri, ada göre sıralı. Aktif tenant yoksa boş.</summary>
    Task<IReadOnlyList<TeamInfo>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Verilen kullanıcıların birincil ekibi (kullanıcı → ekip). Hiçbir ekipte olmayan kullanıcı sonuçta yoktur.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> PrimaryTeamsOfUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}
