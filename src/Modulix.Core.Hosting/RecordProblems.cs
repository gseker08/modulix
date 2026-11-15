using Microsoft.AspNetCore.Http;
using Modulix.Core.Contracts.Records;

namespace Modulix.Core.Hosting;

/// <summary>Kayıt boru hattı hatasını HTTP yanıtına çevirir; host'un genel kayıt uçları ve modül uç noktaları aynı biçimi kullanır.</summary>
public static class RecordProblems
{
    /// <summary>Doğrulama 422 (alan hatalarıyla), bulunamadı 404, çakışma 409, diğerleri (yetki) 403; <c>code</c> uzantısı hata kodudur.</summary>
    public static IResult ToProblem(RecordError error)
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
