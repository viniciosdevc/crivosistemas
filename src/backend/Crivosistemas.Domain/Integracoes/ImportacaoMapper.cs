using System.Text.Json;
using Crivosistemas.Domain.Vendas;

namespace Crivosistemas.Domain.Integracoes;

/// <summary>Converte as linhas de <c>GET /api/export/*</c> do crivonft (formato de docs/API.md) no mesmo payload dos eventos,
/// para a reconciliação reaproveitar a regra de upsert. Só <c>vendas_nft</c> sai por esta rota; câmbios e site chegam por eventos.</summary>
public static class ImportacaoMapper
{
    /// <summary>pendente/entregue/cancelado (vendas_nft) para o status normalizado do contrato.</summary>
    public static StatusVenda StatusNft(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "entregue" => StatusVenda.Concluido,
        "cancelado" => StatusVenda.Cancelado,
        _ => StatusVenda.Pendente,
    };

    /// <summary>Linha de vendas exportada. Devolve null (e o motivo em <paramref name="ignorada"/>) quando não deve ser importada.</summary>
    public static ResultadoParse<VendaUpsertPayload> Venda(JsonElement r, out string? ignorada)
    {
        ignorada = null;
        var e = new List<string>();
        var id = PayloadParser.Texto(r, "id", e, "id", max: 64);
        var pedido = PayloadParser.Texto(r, "pedido_id", e, "pedido_id", obrigatorio: false, max: 64);
        if (pedido is not null)
        {
            // A linha de vendas_nft que espelha um pedido do site já chega como venda "site" por evento; importar de novo contaria duas vezes.
            ignorada = "espelho de pedido do site";
            return new(null, []);
        }
        if (PayloadParser.Texto(r, "external_id", e, "external_id", obrigatorio: false, max: 120) is { } ext && ext.StartsWith(VendaEnvioRegras.PrefixoExternalId, StringComparison.Ordinal))
        {
            // Venda lançada aqui e espelhada no crivonft: já existe como "manual"; importar de novo duplicaria.
            ignorada = "espelho de venda manual deste sistema";
            return new(null, []);
        }
        var dataHora = PayloadParser.Data(r, "data_hora", e, "data_hora", obrigatorio: true);
        var status = PayloadParser.Texto(r, "status", e, "status", obrigatorio: false, max: 60) ?? "pendente";
        var atualizado = PayloadParser.Data(r, "atualizado_em", e, "atualizado_em", obrigatorio: false) ?? dataHora;
        FormaPagamento? forma = null;
        if (PayloadParser.Texto(r, "forma_pagamento", e, "forma_pagamento", obrigatorio: false) is { } ft && Nomes.TentarForma(ft, out var f)) forma = f;
        decimal N(string n) => PayloadParser.Numero(r, n, e, n) ?? 0;
        var nick = PayloadParser.Texto(r, "nickname_comprador", e, "nickname_comprador", obrigatorio: false, max: 120);
        var item = PayloadParser.Texto(r, "nome_item", e, "nome_item", obrigatorio: false, max: 300) ?? "(sem descrição)";
        var receita = PayloadParser.Dinheiro(N("receita_total_brl"));

        if (e.Count > 0) return new(null, e);
        var valores = new ValoresPayload(receita, 0, 0, PayloadParser.Dinheiro(N("valor_venda_pix")), N("valor_venda_cambios"),
            PayloadParser.Dinheiro(N("custo_brl")), N("custo_usd"), N("custo_cambios"), PayloadParser.Dinheiro(N("lucro_brl")), PayloadParser.Dinheiro(N("margem_percent")));
        var vendedor = PayloadParser.Texto(r, "vendedor", e, "vendedor", obrigatorio: false, max: 120);
        return new(new VendaUpsertPayload(OrigemVenda.Nft, id!, null, dataHora!.Value, status, StatusNft(status), forma, vendedor,
            nick is null ? null : new ClientePayload(nick, nick, null, null, null),
            [new ItemPayload(item, 1, 0, receita, null)], valores, null, atualizado!.Value), []);
    }

    /// <summary>Linha de clientes exportada: por nickname.</summary>
    public static ResultadoParse<ClientePayload> Cliente(JsonElement r)
    {
        var e = new List<string>();
        var nick = PayloadParser.Texto(r, "nickname", e, "nickname", max: 120);
        var primeira = PayloadParser.Data(r, "primeira_compra", e, "primeira_compra", obrigatorio: false);
        var carteira = PayloadParser.CarteiraTexto(r, "carteira");
        bool? favorito = r.TryGetProperty("favorito", out var f) && f.ValueKind is JsonValueKind.True or JsonValueKind.False ? f.GetBoolean() : null;
        return e.Count > 0 ? new(null, e) : new(new ClientePayload(nick!, nick!, null, null, primeira, carteira, favorito), []);
    }
}
