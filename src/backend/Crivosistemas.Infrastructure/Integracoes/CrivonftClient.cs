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
    Task<PrecificacaoContrato.PayloadDto> LerPrecificacaoAsync(CancellationToken ct);
    Task<EnvioResposta> EnviarPrecificacaoAsync(string corpoJson, CancellationToken ct);
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
}
