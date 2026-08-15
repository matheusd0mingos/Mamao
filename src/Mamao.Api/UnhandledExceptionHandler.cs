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

        // Requisicao malformada nao e falha do servidor. O ASP.NET Core lanca
        // BadHttpRequestException quando falta um parametro obrigatorio ou o corpo nao
        // converte, e ela JA carrega o status certo — 400. Transformar isso em 500 mente
        // duas vezes: diz ao cliente que a culpa e nossa, e enche o log de erro com engano
        // de digitacao, escondendo a falha de verdade no meio.
        if (exception is BadHttpRequestException requisicaoRuim)
        {
            logger.LogInformation(
                "Requisicao invalida em {Method} {Path}: {Motivo}",
                httpContext.Request.Method, httpContext.Request.Path, requisicaoRuim.Message);

            var invalida = new ProblemDetails
            {
                Status = requisicaoRuim.StatusCode,
                Title = "Requisição inválida",
                Detail = "Confira os parâmetros enviados.",
                Extensions =
                {
                    ["traceId"] = traceId,
                    ["code"] = "bad_request",
                },
            };

            httpContext.Response.StatusCode = invalida.Status.Value;
            await httpContext.Response.WriteAsJsonAsync(invalida, cancellationToken);
            return true;
        }

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
