using System.Globalization;
using System.Text.Json;
using Crivosistemas.Domain.Vendas;

namespace Crivosistemas.Domain.Integracoes;

public sealed record ClientePayload(string Chave, string Nickname, string? Email, string? IdentidadeId, DateTime? CriadoEm);

public sealed record ItemPayload(string Descricao, decimal Quantidade, decimal PrecoUsd, decimal PrecoBrl, string? Referencia);

public sealed record ValoresPayload(decimal TotalBrl, decimal DescontoBrl, decimal TaxaBrl, decimal ValorPixBrl, decimal ValorCambios,
    decimal CustoBrl, decimal CustoUsd, decimal CustoCambios, decimal LucroBrl, decimal MargemPercent)
{
    public static readonly ValoresPayload Zerado = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record CupomPayload(string Codigo, TipoCupom Tipo);

public sealed record VendaUpsertPayload(OrigemVenda Origem, string OrigemId, string? PedidoId, DateTime DataHora, string Status,
    StatusVenda StatusNormalizado, FormaPagamento? FormaPagamento, string? Vendedor, ClientePayload? Cliente,
    IReadOnlyList<ItemPayload> Itens, ValoresPayload Valores, CupomPayload? Cupom, DateTime AtualizadoEm);

public sealed record VendaRemovidaPayload(OrigemVenda Origem, string OrigemId);

public sealed record EventoEnvelope(Guid Id, string Tipo, DateTime OcorridoEm, JsonElement Dados);

public sealed record ResultadoParse<T>(T? Valor, IReadOnlyList<string> Erros) where T : class
{
    public bool Ok => Valor is not null && Erros.Count == 0;
}

public static class TiposEvento
{
    public const string VendaUpsert = "venda.upsert";
    public const string VendaRemovida = "venda.removida";
    public const string ClienteUpsert = "cliente.upsert";

    public static bool Conhecido(string? tipo) => tipo is VendaUpsert or VendaRemovida or ClienteUpsert;
}

/// <summary>Leitura tolerante do JSON do contrato (números aceitos como número ou texto). Acumula todos os erros em vez de parar no primeiro.</summary>
public static class PayloadParser
{
    public static ResultadoParse<EventoEnvelope> Envelope(string json)
    {
        var erros = new List<string>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return new(null, ["O corpo não é um JSON válido."]); }

        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return new(null, ["O corpo deve ser um objeto JSON."]);

            Guid id = default;
            var idTexto = Texto(raiz, "id", erros, "id");
            if (idTexto is not null && !Guid.TryParse(idTexto, out id)) erros.Add("id deve ser um UUID.");
            var tipo = Texto(raiz, "tipo", erros, "tipo");
            if (tipo is not null && !TiposEvento.Conhecido(tipo)) erros.Add($"tipo desconhecido: {tipo}.");
            var ocorrido = Data(raiz, "ocorrido_em", erros, "ocorrido_em", obrigatorio: true);
            JsonElement dados = default;
            if (!raiz.TryGetProperty("dados", out var d) || d.ValueKind != JsonValueKind.Object) erros.Add("dados deve ser um objeto.");
            else dados = d.Clone();

            return erros.Count > 0 ? new(null, erros) : new(new EventoEnvelope(id, tipo!, ocorrido!.Value, dados), []);
        }
    }

    public static ResultadoParse<VendaUpsertPayload> VendaUpsert(JsonElement d)
    {
        var e = new List<string>();
        var origem = default(OrigemVenda);
        var origemTexto = Texto(d, "origem", e, "origem");
        if (origemTexto is not null && !Nomes.TentarOrigem(origemTexto, out origem)) e.Add("origem deve ser nft, cambios ou site.");
        var origemId = Texto(d, "origem_id", e, "origem_id", max: 64);
        var pedidoId = Texto(d, "pedido_id", e, "pedido_id", obrigatorio: false, max: 64);
        var dataHora = Data(d, "data_hora", e, "data_hora", obrigatorio: true);
        var status = Texto(d, "status", e, "status", obrigatorio: false, max: 60) ?? "";
        var normTexto = Texto(d, "status_normalizado", e, "status_normalizado");
        var norm = default(StatusVenda);
        if (normTexto is not null && !Nomes.TentarStatus(normTexto, out norm)) e.Add("status_normalizado inválido.");
        FormaPagamento? forma = null;
        var formaTexto = Texto(d, "forma_pagamento", e, "forma_pagamento", obrigatorio: false);
        if (formaTexto is not null)
        {
            if (Nomes.TentarForma(formaTexto, out var f)) forma = f; else e.Add("forma_pagamento deve ser pix, cambios, hibrido ou null.");
        }
        var vendedor = Texto(d, "vendedor", e, "vendedor", obrigatorio: false, max: 120);

        ClientePayload? cliente = null;
        if (d.TryGetProperty("cliente", out var c) && c.ValueKind != JsonValueKind.Null)
        {
            if (c.ValueKind != JsonValueKind.Object) e.Add("cliente deve ser um objeto ou null.");
            else cliente = Cliente(c, e, "cliente");
        }

        var itens = new List<ItemPayload>();
        if (d.TryGetProperty("itens", out var its) && its.ValueKind != JsonValueKind.Null)
        {
            if (its.ValueKind != JsonValueKind.Array) e.Add("itens deve ser uma lista.");
            else
            {
                var i = 0;
                foreach (var it in its.EnumerateArray())
                {
                    var p = $"itens[{i++}]";
                    if (it.ValueKind != JsonValueKind.Object) { e.Add($"{p} deve ser um objeto."); continue; }
                    itens.Add(new ItemPayload(
                        Texto(it, "descricao", e, p + ".descricao", max: 300) ?? "",
                        Numero(it, "quantidade", e, p + ".quantidade") ?? 1,
                        Numero(it, "preco_usd", e, p + ".preco_usd") ?? 0,
                        Dinheiro(Numero(it, "preco_brl", e, p + ".preco_brl") ?? 0),
                        Texto(it, "referencia", e, p + ".referencia", obrigatorio: false, max: 120)));
                }
            }
        }

        var valores = ValoresPayload.Zerado;
        if (!d.TryGetProperty("valores", out var v) || v.ValueKind != JsonValueKind.Object) e.Add("valores deve ser um objeto.");
        else
        {
            decimal N(string n) => Numero(v, n, e, "valores." + n) ?? 0;
            valores = new ValoresPayload(Dinheiro(N("total_brl")), Dinheiro(N("desconto_brl")), Dinheiro(N("taxa_brl")), Dinheiro(N("valor_pix_brl")),
                N("valor_cambios"), Dinheiro(N("custo_brl")), N("custo_usd"), N("custo_cambios"), Dinheiro(N("lucro_brl")), Dinheiro(N("margem_percent")));
        }

        CupomPayload? cupom = null;
        if (d.TryGetProperty("cupom", out var cp) && cp.ValueKind != JsonValueKind.Null)
        {
            if (cp.ValueKind != JsonValueKind.Object) e.Add("cupom deve ser um objeto ou null.");
            else
            {
                var codigo = Texto(cp, "codigo", e, "cupom.codigo", max: 60);
                var tipoTexto = Texto(cp, "tipo", e, "cupom.tipo");
                if (tipoTexto is not null && !Nomes.TentarCupom(tipoTexto, out var tipo)) e.Add("cupom.tipo deve ser percentual, valor_fixo ou taxa_gratis.");
                else if (codigo is not null && tipoTexto is not null) { Nomes.TentarCupom(tipoTexto, out tipo); cupom = new CupomPayload(codigo, tipo); }
            }
        }

        var atualizado = Data(d, "atualizado_em", e, "atualizado_em", obrigatorio: true);

        if (e.Count > 0) return new(null, e);
        return new(new VendaUpsertPayload(origem, origemId!, pedidoId, dataHora!.Value, status, norm, forma, vendedor, cliente, itens, valores, cupom, atualizado!.Value), []);
    }

    public static ResultadoParse<VendaRemovidaPayload> VendaRemovida(JsonElement d)
    {
        var e = new List<string>();
        var origem = default(OrigemVenda);
        var origemTexto = Texto(d, "origem", e, "origem");
        if (origemTexto is not null && !Nomes.TentarOrigem(origemTexto, out origem)) e.Add("origem deve ser nft, cambios ou site.");
        var origemId = Texto(d, "origem_id", e, "origem_id", max: 64);
        return e.Count > 0 ? new(null, e) : new(new VendaRemovidaPayload(origem, origemId!), []);
    }

    public static ResultadoParse<ClientePayload> ClienteUpsert(JsonElement d)
    {
        var e = new List<string>();
        var c = Cliente(d, e, "");
        return e.Count > 0 || c is null ? new(null, e) : new(c, []);
    }

    private static ClientePayload? Cliente(JsonElement c, List<string> e, string prefixo)
    {
        string P(string n) => prefixo.Length == 0 ? n : prefixo + "." + n;
        var chave = Texto(c, "chave", e, P("chave"), max: 120);
        var nickname = Texto(c, "nickname", e, P("nickname"), obrigatorio: false, max: 120);
        var email = Texto(c, "email", e, P("email"), obrigatorio: false, max: 254);
        var identidade = Texto(c, "identidade_id", e, P("identidade_id"), obrigatorio: false, max: 120);
        var criado = Data(c, "criado_em", e, P("criado_em"), obrigatorio: false);
        return chave is null ? null : new ClientePayload(chave, nickname ?? chave, email, identidade, criado);
    }

    public static decimal Dinheiro(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    // ── leitores ────────────────────────────────────────────────────────────

    public static string? Texto(JsonElement o, string prop, List<string> erros, string caminho, bool obrigatorio = true, int max = 200)
    {
        if (!o.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (obrigatorio) erros.Add($"{caminho} é obrigatório.");
            return null;
        }
        if (v.ValueKind != JsonValueKind.String)
        {
            // Aceita número onde o contrato diz "texto" (ids numéricos), mas não objetos.
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            erros.Add($"{caminho} deve ser texto.");
            return null;
        }
        var s = v.GetString()!.Trim();
        if (s.Length == 0)
        {
            if (obrigatorio) erros.Add($"{caminho} não pode ser vazio.");
            return null;
        }
        if (s.Length > max) { erros.Add($"{caminho} é longo demais (máx. {max})."); return null; }
        return s;
    }

    public static decimal? Numero(JsonElement o, string prop, List<string> erros, string caminho)
    {
        if (!o.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return s;
        erros.Add($"{caminho} deve ser um número.");
        return null;
    }

    public static DateTime? Data(JsonElement o, string prop, List<string> erros, string caminho, bool obrigatorio)
    {
        if (!o.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (obrigatorio) erros.Add($"{caminho} é obrigatório.");
            return null;
        }
        if (v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return DateTime.SpecifyKind(dt.UtcDateTime, DateTimeKind.Utc);
        erros.Add($"{caminho} deve ser uma data ISO-8601.");
        return null;
    }
}
