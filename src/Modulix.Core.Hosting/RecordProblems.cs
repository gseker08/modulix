using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modulix.Core.Contracts.Localization;
using Modulix.Core.Contracts.Records;

namespace Modulix.Core.Hosting;

/// <summary>Kayıt boru hattı hatasını HTTP yanıtına çevirir; host'un genel kayıt uçları ve modül uç noktaları aynı biçimi kullanır.</summary>
public static class RecordProblems
{
    /// <summary>
    /// Kayıt hatasının HTTP karşılığı: doğrulama 422 (alan hatalarıyla), bulunamadı 404, çakışma 409, diğerleri (yetki) 403.
    /// Anahtarlı iletiler (ADR-0034 §6) yanıt yazılırken isteğin diline çevrilir; <c>code</c>, alan adları ve yanıtın biçimi dile
    /// göre değişmez. Anahtarsız hata dil çözülmeden olduğu gibi döner.
    /// </summary>
    public static IResult ToProblem(RecordError error) =>
        IsTranslatable(error) ? new TranslatedRecordProblem(error) : ToHttp(error);

    private static bool IsTranslatable(RecordError error) =>
        error.MessageKey is not null || error.FieldErrors?.Any(e => e.MessageKey is not null) == true;

    private sealed class TranslatedRecordProblem(RecordError error) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var localizer = httpContext.RequestServices.GetRequiredService<ILocalizer>();
            var cancellationToken = httpContext.RequestAborted;

            List<RecordFieldError>? fieldErrors = null;
            if (error.FieldErrors is { } source)
            {
                fieldErrors = [];
                foreach (var fieldError in source)
                {
                    fieldErrors.Add(fieldError.MessageKey is null
                        ? fieldError
                        : fieldError with
                        {
                            Message = await TextAsync(localizer, fieldError.MessageKey, fieldError.MessageArgs, fieldError.Message, cancellationToken),
                        });
                }
            }

            var translated = error with
            {
                Message = await TextAsync(localizer, error.MessageKey, error.MessageArgs, error.Message, cancellationToken),
                FieldErrors = fieldErrors,
            };
            await ToHttp(translated).ExecuteAsync(httpContext);
        }

        // Anahtarsız ileti, sözlükte bulunmayan anahtar ya da yer tutucusu uymayan metin Türkçe kaynak metne düşer (ILocalizer kuralı).
        private static async ValueTask<string> TextAsync(
            ILocalizer localizer, string? key, IReadOnlyList<object?>? args, string fallback, CancellationToken cancellationToken) =>
            key is null ? fallback : await localizer.TextAsync(new ServerMessage(fallback, key, [.. args ?? []]), cancellationToken);
    }

    private static IResult ToHttp(RecordError error)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code.ToString() };

        return error.Code switch
        {
            RecordErrorCode.ValidationFailed => Results.ValidationProblem(
                (error.FieldErrors ?? Array.Empty<RecordFieldError>())
                    .GroupBy(e => e.Field)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()),
                title: error.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: extensions),
            RecordErrorCode.NotFound or RecordErrorCode.UnknownEntity =>
                Results.Problem(statusCode: StatusCodes.Status404NotFound, title: error.Message, extensions: extensions),
            RecordErrorCode.Conflict =>
                Results.Problem(statusCode: StatusCodes.Status409Conflict, title: error.Message, extensions: extensions),
            _ => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: error.Message, extensions: extensions),
        };
    }
}
