using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Mamao.Api;

/// <summary>
/// Toda falha nao tratada vira ProblemDetails com o traceId. O usuario reporta o id, voce
/// acha o trace — sem isso, investigar erro de cliente vira arqueologia.
/// </summary>
public sealed class UnhandledExceptionHandler(
    IHostEnvironment environment,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        logger.LogError(exception, "Falha nao tratada em {Method} {Path}. TraceId {TraceId}.",
            httpContext.Request.Method, httpContext.Request.Path, traceId);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Erro interno",
            Detail = environment.IsDevelopment()
                ? exception.Message
                : "Algo deu errado do nosso lado. Se persistir, informe o codigo abaixo.",
            Extensions =
            {
                ["traceId"] = traceId,
                ["code"] = "internal_error",
            },
        };

        httpContext.Response.StatusCode = problem.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
