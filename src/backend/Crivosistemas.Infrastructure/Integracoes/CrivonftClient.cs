using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Precificacao;
using Microsoft.Extensions.Options;

namespace Crivosistemas.Infrastructure.Integracoes;

public sealed record ExportPagina(int Total, int Limit, int Offset, IReadOnlyList<JsonElement> Data);

/// <summary>Resposta do crivonft ao PUT de precificação. <see cref="StatusHttp"/> é o código HTTP; falha de rede lança <see cref="CrivonftException"/>.</summary>
public sealed record EnvioResposta(int StatusHttp, bool Aplicada, int? Versao, string? Corpo)
{
    public bool Sucesso => StatusHttp is >= 200 and < 300;
}

/// <summary>Resposta de <c>POST /api/integracoes/gestao/reenviar</c>: quantas vendas de cada origem foram reenfileiradas no crivonft.</summary>
public sealed record ReenvioResposta(int Nft, int Cambios, int Site, bool Truncado)
{
    public int Total => Nft + Cambios + Site;
}

public sealed class CrivonftException(string message, int? statusHttp = null, Exception? inner = null) : Exception(message, inner)
{
    public int? StatusHttp { get; } = statusHttp;
}

/// <summary>Cliente HTTP do crivonft. É uma interface para os testes trocarem por um falso.</summary>
public interface ICrivonftClient
{
    bool EnvioConfigurado { get; }
    bool ExportConfigurado { get; }
    Task<ExportPagina> ExportarVendasAsync(DateOnly? de, DateOnly? ate, int limit, int offset, CancellationToken ct);
    Task<ExportPagina> ExportarClientesAsync(int limit, int offset, CancellationToken ct);
    /// <summary>Lotes do estoque do crivonft: <paramref name="tipo"/> "usd" ou "esmeralda".</summary>
    Task<ExportPagina> ExportarEstoqueAsync(string tipo, int limit, int offset, CancellationToken ct);
    /// <summary>Imagem dos itens pelo nome (estoque normal e catálogo do crivonft); nome sem imagem vem com null. Até 100 nomes.</summary>
    Task<IReadOnlyDictionary<string, string?>> ImagensDeItensAsync(IReadOnlyList<string> nomes, CancellationToken ct);
    Task<ReenvioResposta> ReenviarPeriodoAsync(DateOnly de, DateOnly ate, CancellationToken ct);
    Task<PrecificacaoContrato.PayloadDto> LerPrecificacaoAsync(CancellationToken ct);
    Task<EnvioResposta> EnviarPrecificacaoAsync(string corpoJson, CancellationToken ct);
    /// <summary>Convites da loja (<c>/api/integracoes/gestao/convites</c>): GET lista (corpo null), POST executa uma ação. Devolve o JSON do crivonft; qualquer não-2xx lança <see cref="CrivonftException"/>.</summary>
    Task<string> ConvitesAsync(string? corpoJson, CancellationToken ct);
}

public sealed class CrivonftClient(HttpClient http, IOptions<CrivonftOptions> options) : ICrivonftClient
{
    private const string RotaPrecificacao = "/api/integracoes/gestao/precificacao";
    private CrivonftOptions O => options.Value;

    public bool EnvioConfigurado => O.Envia;
    public bool ExportConfigurado => O.Exporta;

    public async Task<ExportPagina> ExportarVendasAsync(DateOnly? de, DateOnly? ate, int limit, int offset, CancellationToken ct)
    {
        var q = new List<string> { $"limit={limit}", $"offset={offset}" };
        if (de is { } d) q.Add("de=" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (ate is { } a) q.Add("ate=" + a.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return await ExportarAsync("/api/export/vendas?" + string.Join('&', q), ct);
    }

    public Task<ExportPagina> ExportarClientesAsync(int limit, int offset, CancellationToken ct) =>
        ExportarAsync($"/api/export/clientes?limit={limit}&offset={offset}", ct);

    public Task<ExportPagina> ExportarEstoqueAsync(string tipo, int limit, int offset, CancellationToken ct) =>
        ExportarAsync($"/api/export/estoque/{(tipo == "usd" ? "usd" : "esmeralda")}?limit={limit}&offset={offset}", ct);

    public async Task<IReadOnlyDictionary<string, string?>> ImagensDeItensAsync(IReadOnlyList<string> nomes, CancellationToken ct)
    {
        var vazio = nomes.Distinct().ToDictionary(n => n, _ => (string?)null);
        if (!O.Exporta || nomes.Count == 0) return vazio;
        var caminho = "/api/export/nft/imagens?" + string.Join('&', nomes.Take(100).Select(n => "nome=" + Uri.EscapeDataString(n)));
        using var req = new HttpRequestMessage(HttpMethod.Get, Url(caminho));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", O.ExportToken);
        var (status, corpo) = await EnviarAsync(req, ct);
        if (status is < 200 or >= 300) throw new CrivonftException($"O crivonft respondeu {status} em /api/export/nft/imagens: {Resumo(corpo)}", status);
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.TryGetProperty("imagens", out var img) && img.ValueKind == JsonValueKind.Object)
                foreach (var p in img.EnumerateObject())
                    vazio[p.Name] = p.Value.ValueKind == JsonValueKind.String && Uri.TryCreate(p.Value.GetString(), UriKind.Absolute, out var u) && u.Scheme is "https" or "http" ? p.Value.GetString() : null;
            return vazio;
        }
        catch (JsonException e)
        {
            throw new CrivonftException("Resposta de imagens do crivonft não é um JSON válido.", status, e);
        }
    }

    private async Task<ExportPagina> ExportarAsync(string caminho, CancellationToken ct)
    {
        if (!O.Exporta) throw new CrivonftException("Integração com o crivonft não configurada (BaseUrl e ExportToken).");
        using var req = new HttpRequestMessage(HttpMethod.Get, Url(caminho));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", O.ExportToken);
        var (status, corpo) = await EnviarAsync(req, ct);
        if (status is < 200 or >= 300) throw new CrivonftException($"O crivonft respondeu {status} em {caminho.Split('?')[0]}: {Resumo(corpo)}", status);
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            var r = doc.RootElement;
            var dados = r.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array ? d.EnumerateArray().Select(x => x.Clone()).ToList() : [];
            int Int(string n) => r.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) ? i : 0;
            return new ExportPagina(Int("total"), Int("limit"), Int("offset"), dados);
        }
        catch (JsonException e)
        {
            throw new CrivonftException("Resposta de exportação do crivonft não é um JSON válido.", status, e);
        }
    }

    /// <summary>Pede ao crivonft que reenfileire as vendas (as 3 origens) e os clientes do período pela outbox dele, com payload completo.
    /// Os eventos chegam depois, pelo endpoint de eventos. 404 = crivonft sem a rota (desatualizado).</summary>
    public async Task<ReenvioResposta> ReenviarPeriodoAsync(DateOnly de, DateOnly ate, CancellationToken ct)
    {
        ExigirEnvio();
        var corpo = JsonSerializer.SerializeToUtf8Bytes(new { de = de.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ate = ate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        using var req = Assinada(HttpMethod.Post, "/api/integracoes/gestao/reenviar", corpo);
        var (status, resp) = await EnviarAsync(req, ct);
        if (status is < 200 or >= 300) throw new CrivonftException($"O crivonft respondeu {status} em /api/integracoes/gestao/reenviar: {Resumo(resp)}", status);
        try
        {
            using var doc = JsonDocument.Parse(resp);
            var r = doc.RootElement;
            int Int(string n) => r.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) ? i : 0;
            return new ReenvioResposta(Int("nft"), Int("cambios"), Int("site"), r.TryGetProperty("truncado", out var t) && t.ValueKind == JsonValueKind.True);
        }
        catch (JsonException e)
        {
            throw new CrivonftException("Resposta de reenvio do crivonft não é um JSON válido.", status, e);
        }
    }

    public async Task<PrecificacaoContrato.PayloadDto> LerPrecificacaoAsync(CancellationToken ct)
    {
        ExigirEnvio();
        using var req = Assinada(HttpMethod.Get, RotaPrecificacao, []);
        var (status, corpo) = await EnviarAsync(req, ct);
        if (status is < 200 or >= 300) throw new CrivonftException($"O crivonft respondeu {status} ao ler a precificação: {Resumo(corpo)}", status);
        try { return PrecificacaoContrato.Ler(corpo) ?? throw new CrivonftException("Resposta de precificação vazia."); }
        catch (JsonException e) { throw new CrivonftException("Resposta de precificação do crivonft não é um JSON válido.", status, e); }
    }

    public async Task<EnvioResposta> EnviarPrecificacaoAsync(string corpoJson, CancellationToken ct)
    {
        ExigirEnvio();
        using var req = Assinada(HttpMethod.Put, RotaPrecificacao, Encoding.UTF8.GetBytes(corpoJson));
        var (status, corpo) = await EnviarAsync(req, ct);
        bool aplicada = false;
        int? versao = null;
        if (status is >= 200 and < 300)
        {
            try
            {
                using var doc = JsonDocument.Parse(corpo);
                aplicada = doc.RootElement.TryGetProperty("aplicada", out var a) && a.ValueKind == JsonValueKind.True;
                versao = doc.RootElement.TryGetProperty("versao", out var v) && v.TryGetInt32(out var i) ? i : null;
            }
            catch (JsonException) { /* corpo fora do padrão: o status 2xx basta */ }
        }
        return new EnvioResposta(status, aplicada, versao, corpo);
    }

    public async Task<string> ConvitesAsync(string? corpoJson, CancellationToken ct)
    {
        ExigirEnvio();
        using var req = Assinada(corpoJson is null ? HttpMethod.Get : HttpMethod.Post, "/api/integracoes/gestao/convites", corpoJson is null ? [] : Encoding.UTF8.GetBytes(corpoJson));
        var (status, corpo) = await EnviarAsync(req, ct);
        if (status is < 200 or >= 300) throw new CrivonftException($"O crivonft respondeu {status} em /api/integracoes/gestao/convites: {Resumo(corpo)}", status);
        return corpo;
    }

    private void ExigirEnvio()
    {
        if (!O.Envia) throw new CrivonftException("Integração com o crivonft não configurada (BaseUrl, Token e HmacSecret).");
    }

    private string Url(string caminho) => O.BaseUrl.TrimEnd('/') + caminho;

    /// <summary>Requisição com Bearer + HMAC. O corpo assinado é exatamente o enviado (GET: vazio).</summary>
    private HttpRequestMessage Assinada(HttpMethod metodo, string caminho, byte[] corpo)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var req = new HttpRequestMessage(metodo, Url(caminho));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", O.Token);
        req.Headers.Add(CrivoAuth.CabecalhoTimestamp, ts);
        req.Headers.Add(CrivoAuth.CabecalhoAssinatura, CrivoAuth.Assinar(O.HmacSecret, ts, corpo));
        if (corpo.Length > 0)
        {
            req.Content = new ByteArrayContent(corpo);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }
        return req;
    }

    private async Task<(int Status, string Corpo)> EnviarAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            using var res = await http.SendAsync(req, ct);
            return ((int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new CrivonftException("Falha de rede ao falar com o crivonft: " + e.Message, null, e);
        }
    }

    private static string Resumo(string corpo) => corpo.Length <= 300 ? corpo : corpo[..300] + "…";

    /// <summary>Texto claro, em português, para o que deu errado ao falar com o crivonft (vai no detalhe do problema 502 e na reconciliação).</summary>
    public static string Explicar(CrivonftException e) => e.StatusHttp switch
    {
        404 => "O crivonft ainda não tem a rota de integração; publique a atualização da loja.",
        401 or 403 => "Token ou segredo diferentes nos dois sistemas.",
        null => "Não foi possível falar com o crivonft (sem resposta ou tempo esgotado). " + e.Message,
        { } s when s >= 500 => $"O crivonft respondeu com erro {s}. Tente de novo em instantes.",
        { } s => $"O crivonft recusou a chamada ({s}). {e.Message}",
    };
}
