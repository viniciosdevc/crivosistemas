using Crivosistemas.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Common;

/// <summary>Registro inexistente.</summary>
public sealed class NotFoundException(string message = "Registro não encontrado.") : Exception(message);

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problems)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title, code) = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, e.Message, "not_found"),
            ConflictException e => (StatusCodes.Status409Conflict, e.Message, e.Code),
            DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Message, e.Code),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
                "Este registro foi alterado ao mesmo tempo por outra operação. Atualize e tente novamente.", "concurrency"),
            BadHttpRequestException e => (e.StatusCode, "Requisição inválida.", "bad_request"),
            _ => (StatusCodes.Status500InternalServerError, "Ocorreu um erro inesperado. Tente novamente.", "internal_error"),
        };

        if (status >= 500) logger.LogError(exception, "Exceção não tratada em {Method} {Path}", http.Request.Method, http.Request.Path);
        else if (exception is DbUpdateConcurrencyException) logger.LogWarning(exception, "Conflito de concorrência em {Method} {Path}", http.Request.Method, http.Request.Path);

        http.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["code"] = code, ["traceId"] = http.TraceIdentifier },
            },
        });
    }
}
