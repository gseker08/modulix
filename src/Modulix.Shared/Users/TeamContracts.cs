namespace Modulix.Shared.Users;

// Ekipler (/api/teams, Y8). Ekip, "ekibinin kayıtları" kapsamındaki kişinin hangi kayıtları göreceğini belirler.

public sealed record TeamDto(Guid Id, string Name, string? Description, int MemberCount, bool IsPrimary = false);

public sealed record TeamMemberDto(Guid MembershipId, Guid UserId, string DisplayName, string Email);

public sealed record TeamDetailDto(Guid Id, string Name, string? Description, IReadOnlyList<TeamMemberDto> Members);

public sealed record SaveTeamRequest(string Name, string? Description);

public sealed record SetTeamMembersRequest(IReadOnlyList<Guid> MembershipIds);

public sealed record SetMembershipTeamsRequest(IReadOnlyList<Guid> TeamIds);

/// <summary>Kişinin birincil ekibi (rapor kırılımı); <c>TeamId</c> null seçimi kaldırır (en önce katıldığı ekip geçerli olur).</summary>
public sealed record SetPrimaryTeamRequest(Guid? TeamId);
