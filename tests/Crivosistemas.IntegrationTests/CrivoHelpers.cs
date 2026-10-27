using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Crivosistemas.Domain.Integracoes;

namespace Crivosistemas.IntegrationTests;

/// <summary>Monta e assina eventos como o crivonft faz.</summary>
public static class CrivoHelpers
{
    public static string Evento(string tipo, object dados, Guid? id = null, DateTime? ocorridoEm = null) =>
        JsonSerializer.Serialize(new { id = id ?? Guid.NewGuid(), tipo, ocorrido_em = (ocorridoEm ?? DateTime.UtcNow).ToString("O"), dados });

    public static object Venda(string origemId, DateTime dataHora, decimal total, string statusNormalizado = "concluido", string origem = "nft", string? nick = "cliente-a",
        string? forma = "pix", DateTime? atualizadoEm = null, decimal lucro = 10, string? cupom = null, string descricao = "NFT raro", decimal custoUsd = 0) => new
        {
            origem,
            origem_id = origemId,
            pedido_id = (string?)null,
            data_hora = dataHora.ToString("O"),
            status = statusNormalizado == "concluido" ? "entregue" : statusNormalizado,
            status_normalizado = statusNormalizado,
            forma_pagamento = forma,
            vendedor = "ana",
            cliente = nick is null ? null : new { chave = nick, nickname = nick, email = (string?)null, identidade_id = (string?)null },
            itens = new[] { new { descricao, quantidade = 1, preco_usd = 10, preco_brl = total, referencia = (string?)null } },
            valores = new
            {
                total_brl = total, desconto_brl = 0, taxa_brl = 0, valor_pix_brl = total, valor_cambios = 0, custo_brl = total - lucro, custo_usd = custoUsd, custo_cambios = 0,
                lucro_brl = lucro, margem_percent = 12.5,
            },
            cupom = cupom is null ? null : new { codigo = cupom, tipo = "percentual" },
            atualizado_em = (atualizadoEm ?? dataHora).ToString("O"),
        };

    public static HttpRequestMessage Requisicao(string corpo, string? token = ApiFactory.CrivoToken, string segredo = ApiFactory.CrivoSegredo, long? timestamp = null,
        string? assinaturaFixa = null)
    {
        var ts = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/integracoes/crivonft/eventos")
        {
            Content = new StringContent(corpo, new UTF8Encoding(false), "application/json"),
        };
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add(CrivoAuth.CabecalhoTimestamp, ts);
        req.Headers.Add(CrivoAuth.CabecalhoAssinatura, assinaturaFixa ?? CrivoAuth.Assinar(segredo, ts, corpo));
        return req;
    }

    /// <summary>Cliente "máquina": sem cookie e sem o cabeçalho anti-CSRF, como o crivonft.</summary>
    public static HttpClient Maquina(this ApiFactory f) => f.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });

    /// <summary>Cliente com cookie e cabeçalho anti-CSRF, já logado (o usuário precisa existir no banco compartilhado).</summary>
    public static async Task<HttpClient> LoginComo(this Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> f, string email)
    {
        var c = f.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        c.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        (await c.Post("/api/auth/login", new { email, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        return c;
    }

    public static async Task<(HttpStatusCode Status, JsonElement Corpo)> Enviar(this HttpClient c, string corpo, HttpRequestMessage? req = null)
    {
        var r = await c.SendAsync(req ?? Requisicao(corpo));
        var texto = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, texto.Length == 0 ? default : JsonSerializer.Deserialize<JsonElement>(texto));
    }
}
