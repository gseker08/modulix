using Modulix.Core.Contracts.Records;

namespace Modulix.Core.Lookups;

/// <summary><see cref="ILookupLabels"/> uygulaması: lookup aramasının kimlikten çözümlemesini kullanır.</summary>
public sealed class LookupLabels(ILookupSearch search) : ILookupLabels
{
    public async Task<RecordResult<IReadOnlyList<LookupLabel>>> ResolveAsync(string lookupKey, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var result = await search.ResolveAsync(lookupKey, ids, cancellationToken);
        return result.IsSuccess
            ? RecordResult<IReadOnlyList<LookupLabel>>.Ok([.. result.Value!.Select(i => new LookupLabel(i.Id, i.Label, i.Unavailable))])
            : RecordResult<IReadOnlyList<LookupLabel>>.Fail(result.Error!);
    }
}
