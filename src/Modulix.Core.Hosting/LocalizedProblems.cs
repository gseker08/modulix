using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Localization;

namespace Modulix.Core.Hosting;

/// <summary>
/// Çevrilen problem ayrıntıları (ADR-0034 §6). Uç nokta süzgeçleri ve <c>ToProblem</c> gibi durumsuz yardımcılar sonucu
/// eşzamanlı döndürür; dil yalnızca yanıt yazılırken, isteğin <see cref="ILocalizer"/>'ıyla çözülür. Host ve modül uç noktaları
/// aynı yardımcıyı kullanır (ADR-0046).
/// </summary>
public static class LocalizedProblems
{
    /// <summary><see cref="Results.Problem(string?, string?, int?, string?, string?, IDictionary{string, object?}?)"/>, başlık ve ayrıntı isteğin dilinde.</summary>
    public static IResult Problem(int statusCode, ServerMessage title, ServerMessage? detail = null, IDictionary<string, object?>? extensions = null) =>
        new LocalizedProblem(statusCode, title, detail, extensions);

    private sealed class LocalizedProblem(int statusCode, ServerMessage title, ServerMessage? detail, IDictionary<string, object?>? extensions) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var localizer = httpContext.RequestServices.GetRequiredService<ILocalizer>();
            var cancellationToken = httpContext.RequestAborted;
            await Results.Problem(
                    statusCode: statusCode,
                    title: await localizer.TextAsync(title, cancellationToken),
                    detail: detail is null ? null : await localizer.TextAsync(detail, cancellationToken),
                    extensions: extensions)
                .ExecuteAsync(httpContext);
        }
    }
}
