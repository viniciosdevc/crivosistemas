namespace Crivosistemas.Api.Common;

/// <summary>Requisições que alteram estado e usam o cookie de sessão precisam de um cabeçalho personalizado. Navegadores não
/// o adicionam em formulários de outros sites e o CORS o bloqueia em outras origens, o que fecha o CSRF além do SameSite.</summary>
public sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Requested-With";

    public Task InvokeAsync(HttpContext http)
    {
        var method = http.Request.Method;
        var safe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
        // O endpoint de eventos do crivonft não usa cookie: autentica por Bearer + HMAC, então não precisa do cabeçalho anti-CSRF.
        var maquina = http.Request.Path.Equals("/api/integracoes/crivonft/eventos", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(method);
        if (!safe && !maquina && http.Request.Path.StartsWithSegments("/api") && !http.Request.Headers.ContainsKey(HeaderName))
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return http.Response.WriteAsJsonAsync(new { title = "Cabeçalho de segurança ausente.", code = "csrf" });
        }
        return next(http);
    }
}
