using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Precificacao;

namespace Crivosistemas.Domain.Vendas;

/// <summary>Fila de envio das vendas manuais ao crivonft (PUT /api/integracoes/gestao/vendas). Uma linha por venda: criar, editar, entregar
/// ou cancelar só recoloca a linha como pendente; o corpo é montado na hora do envio, com o estado atual da venda.</summary>
public class VendaEnvio : Entity
{
    public Guid VendaId { get; set; }
    public EstadoEnvio Estado { get; set; } = EstadoEnvio.Pendente;
    public int Tentativas { get; set; }
    public DateTime ProximaTentativaEm { get; set; } = DateTime.UtcNow;
    public DateTime? UltimaTentativaEm { get; set; }
    public DateTime? EnviadoEm { get; set; }
    /// <summary><see cref="Venda.AtualizadoEmOrigem"/> da versão já entregue; só reenvia quando a venda mudou depois.</summary>
    public DateTime? EnviadoAtualizadoEm { get; set; }
    public int? UltimoStatusHttp { get; set; }
    public string? UltimoErro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public static class VendaEnvioRegras
{
    /// <summary>Prefixo do external_id no crivonft: marca a origem "dash" e deixa o crivonft barrar o retorno da venda.</summary>
    public const string PrefixoExternalId = "dash:";

    public static string ExternalId(Guid vendaId) => PrefixoExternalId + vendaId.ToString("D");

    /// <summary>Status no vocabulário de vendas_nft do crivonft.</summary>
    public static string StatusCrivonft(StatusVenda s) => s switch
    {
        StatusVenda.Concluido or StatusVenda.Pago => "entregue",
        StatusVenda.Cancelado or StatusVenda.Reembolsado => "cancelado",
        _ => "pendente",
    };

    /// <summary>origem_custo aceita no crivonft: estoque_usd, estoque_esmeralda, estoque_normal ou manual (custo em câmbios vira manual).</summary>
    public static string OrigemCustoCrivonft(OrigemCusto? c) => c switch
    {
        OrigemCusto.EstoqueUsd => "estoque_usd",
        OrigemCusto.EstoqueEsmeralda => "estoque_esmeralda",
        _ => "manual",
    };

    /// <summary>Corpo snake_case da rota do crivonft (valores como o dash calculou; o crivonft não recalcula).</summary>
    public static Dictionary<string, object?> Corpo(Venda v)
    {
        var item = v.Itens.OrderBy(i => i.Ordem).FirstOrDefault()?.Descricao ?? "(sem descrição)";
        return new()
        {
            ["external_id"] = VendaEnvioRegras.ExternalId(v.Id),
            ["data_hora"] = Iso(v.DataHora),
            ["atualizado_em"] = Iso(v.AtualizadoEmOrigem),
            ["vendedor"] = v.Vendedor,
            ["nickname_comprador"] = v.Cliente?.Nickname,
            ["nome_item"] = item.Length > 180 ? item[..180] : item,
            ["forma_pagamento"] = Nomes.Forma(v.FormaPagamento ?? FormaPagamento.Pix),
            ["origem_custo"] = VendaEnvioRegras.OrigemCustoCrivonft(v.OrigemCusto),
            ["custo_usd"] = v.CustoUsd,
            ["custo_brl"] = v.CustoBrl,
            ["custo_cambios"] = v.CustoCambios,
            ["valor_venda_pix"] = v.ValorPixBrl,
            ["valor_venda_cambios"] = v.ValorCambios,
            ["receita_total_brl"] = v.TotalBrl,
            ["lucro_brl"] = v.LucroBrl,
            ["margem_percent"] = v.MargemPercent,
            ["status"] = VendaEnvioRegras.StatusCrivonft(v.StatusNormalizado),
        };
    }

    private static string Iso(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("O");
}
