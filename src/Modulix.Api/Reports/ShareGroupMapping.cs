using System.Text.Json;
using Modulix.Core.Reporting;
using Modulix.Shared.Reporting;

namespace Modulix.Api.Reports;

/// <summary>Paylaşım gruplarının (rol, ekip, iş birimi) API gösterimi ile çekirdek türü arasındaki çeviri (ADR-0054 dilim 4a).</summary>
internal static class ShareGroupMapping
{
    /// <summary>İstekteki grupları çevirir; tür tanınmıyorsa ya da kullanıcıysa false (kullanıcılar <c>users</c> ile gelir).</summary>
    public static bool TryRead(IReadOnlyList<ShareGroupDto>? dtos, out List<ShareGroup> groups)
    {
        groups = [];
        foreach (var dto in dtos ?? [])
        {
            if (!Enum.TryParse<ShareTargetKind>(dto.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) || kind == ShareTargetKind.User)
            {
                return false;
            }

            groups.Add(new ShareGroup(kind, dto.Id));
        }

        return true;
    }

    public static IReadOnlyList<ShareGroupDto> ToDto(IReadOnlyList<ShareGroup> groups) =>
        [.. groups.Select(g => new ShareGroupDto(JsonNamingPolicy.CamelCase.ConvertName(g.Kind.ToString()), g.Id))];

    public static IReadOnlyList<ShareGroupOptionDto> ToDto(IReadOnlyList<ShareGroupOption> options) =>
        [.. options.Select(o => new ShareGroupOptionDto(JsonNamingPolicy.CamelCase.ConvertName(o.Kind.ToString()), o.Id, o.Name))];
}
