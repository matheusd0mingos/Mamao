using FluentValidation;
using Mamao.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Mamao.SharedKernel.Web;

/// <summary>
/// Traducao de <see cref="Result"/> para ProblemDetails. Um lugar so, para que o
/// frontend receba sempre a mesma forma de erro e possa tratar por "code" — ver
/// docs/arquitetura/frontend-angular.md.
/// </summary>
public static class HttpResultsExtensions
{
    /// <summary>Codigos de erro que sao 404 em vez de 400.</summary>
    private const string NotFoundSuffix = ".not_found";

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
        => result.IsSuccess
            ? (onSuccess is null ? Microsoft.AspNetCore.Http.Results.Ok(result.Value) : onSuccess(result.Value))
            : Problem(result.Error!);

    public static IResult ToHttpResult(this Result result)
        => result.IsSuccess ? Microsoft.AspNetCore.Http.Results.NoContent() : Problem(result.Error!);

    public static IResult Problem(Error error)
    {
        var status = error.Code.EndsWith(NotFoundSuffix, StringComparison.Ordinal)
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (error.Field is not null)
        {
            // Mesma forma dos erros de validacao, para o formulario consumir os dois
            // caminhos com um codigo so.
            extensions["fieldErrors"] = new Dictionary<string, string[]>
            {
                [ToCamelCase(error.Field)] = [error.Message],
            };
        }

        return Microsoft.AspNetCore.Http.Results.Problem(
            title: status == StatusCodes.Status404NotFound ? "Nao encontrado" : "Requisicao invalida",
            detail: error.Message,
            statusCode: status,
            extensions: extensions);
    }

    /// <summary>
    /// Valida e devolve ProblemDetails com "fieldErrors" quando falhar.
    /// Devolve null quando estiver tudo certo — o endpoint segue o caminho feliz.
    /// </summary>
    public static async Task<IResult?> ValidateToProblemAsync<T>(
        this IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(instance, cancellationToken);
        if (validation.IsValid)
            return null;

        var fieldErrors = validation.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        return Microsoft.AspNetCore.Http.Results.Problem(
            title: "Requisicao invalida",
            detail: "Verifique os campos destacados.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "validation_failed",
                ["fieldErrors"] = fieldErrors,
            });
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}

/// <summary>Atalhos de tipo para as assinaturas dos endpoints.</summary>
public static class ApiResults
{
    public static Ok<T> Ok<T>(T value) => TypedResults.Ok(value);
}
