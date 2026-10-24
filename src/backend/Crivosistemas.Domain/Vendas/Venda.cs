using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Vendas;

public enum OrigemVenda { Nft = 1, Cambios = 2, Site = 3, Manual = 4 }
/// <summary>De onde saiu o custo de uma venda manual (mesmas opções do crivonft, menos o estoque de itens).</summary>
public enum OrigemCusto { EstoqueUsd = 1, Cambios = 2, EstoqueEsmeralda = 3, Manual = 4 }
public enum StatusVenda { Pendente = 1, Pago = 2, Concluido = 3, Cancelado = 4, Reembolsado = 5 }
public enum FormaPagamento { Pix = 1, Cambios = 2, Hibrido = 3 }
public enum TipoCupom { Percentual = 1, ValorFixo = 2, TaxaGratis = 3 }

/// <summary>Nomes de cada enum como trafegam no contrato com o crivonft e na API (minúsculos, sem acento).</summary>
public static class Nomes
{
    public static string Origem(OrigemVenda o) => o switch { OrigemVenda.Nft => "nft", OrigemVenda.Cambios => "cambios", OrigemVenda.Manual => "manual", _ => "site" };
    public static bool TentarOrigem(string? s, out OrigemVenda o)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "nft": o = OrigemVenda.Nft; return true;
            case "cambios": o = OrigemVenda.Cambios; return true;
            case "site": o = OrigemVenda.Site; return true;
            case "manual": o = OrigemVenda.Manual; return true;
            default: o = default; return false;
        }
    }

    public static string Status(StatusVenda s) => s switch
    {
        StatusVenda.Pendente => "pendente", StatusVenda.Pago => "pago", StatusVenda.Concluido => "concluido",
        StatusVenda.Cancelado => "cancelado", _ => "reembolsado",
    };
    public static bool TentarStatus(string? s, out StatusVenda v)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "pendente": v = StatusVenda.Pendente; return true;
            case "pago": v = StatusVenda.Pago; return true;
            case "concluido": v = StatusVenda.Concluido; return true;
            case "cancelado": v = StatusVenda.Cancelado; return true;
            case "reembolsado": v = StatusVenda.Reembolsado; return true;
            default: v = default; return false;
        }
    }

    public static string Forma(FormaPagamento f) => f switch { FormaPagamento.Pix => "pix", FormaPagamento.Cambios => "cambios", _ => "hibrido" };
    public static bool TentarForma(string? s, out FormaPagamento f)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "pix": f = FormaPagamento.Pix; return true;
            case "cambios": f = FormaPagamento.Cambios; return true;
            case "hibrido": f = FormaPagamento.Hibrido; return true;
            default: f = default; return false;
        }
    }

    public static string Custo(OrigemCusto c) => c switch
    {
        OrigemCusto.EstoqueUsd => "estoque_usd", OrigemCusto.Cambios => "cambios", OrigemCusto.EstoqueEsmeralda => "estoque_esmeralda", _ => "manual",
    };
    public static bool TentarCusto(string? s, out OrigemCusto c)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "estoque_usd": c = OrigemCusto.EstoqueUsd; return true;
            case "cambios": c = OrigemCusto.Cambios; return true;
            case "estoque_esmeralda": c = OrigemCusto.EstoqueEsmeralda; return true;
            case "manual": c = OrigemCusto.Manual; return true;
            default: c = default; return false;
        }
    }

    public static string Cupom(TipoCupom t) => t switch { TipoCupom.Percentual => "percentual", TipoCupom.ValorFixo => "valor_fixo", _ => "taxa_gratis" };
    public static bool TentarCupom(string? s, out TipoCupom t)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "percentual": t = TipoCupom.Percentual; return true;
            case "valor_fixo": t = TipoCupom.ValorFixo; return true;
            case "taxa_gratis": t = TipoCupom.TaxaGratis; return true;
            default: t = default; return false;
        }
    }
}

/// <summary>Quem compra. A chave é o nickname ou a identidade do crivonft (único, sem diferenciar maiúsculas).</summary>
public class Cliente : Entity
{
    public required string Chave { get; set; }
    public required string Nickname { get; set; }
    public string? Email { get; set; }
    public string? IdentidadeId { get; set; }
    /// <summary>Endereço da carteira (0x…, minúsculas, Immutable zkEVM), se a origem informou.</summary>
    public string? Carteira { get; set; }
    /// <summary>Quando o cliente nasceu na origem (se informado).</summary>
    public DateTime? CriadoEmOrigem { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

public class Venda : Entity
{
    public OrigemVenda Origem { get; set; }
    public required string OrigemId { get; set; }
    public string? PedidoId { get; set; }
    public DateTime DataHora { get; set; }
    /// <summary>Status original da origem, só para exibição.</summary>
    public string Status { get; set; } = string.Empty;
    public StatusVenda StatusNormalizado { get; set; }
    public FormaPagamento? FormaPagamento { get; set; }
    public string? Vendedor { get; set; }
    public Guid? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public decimal TotalBrl { get; set; }
    public decimal DescontoBrl { get; set; }
    public decimal TaxaBrl { get; set; }
    public decimal ValorPixBrl { get; set; }
    public decimal ValorCambios { get; set; }
    public decimal CustoBrl { get; set; }
    public decimal CustoUsd { get; set; }
    public decimal CustoCambios { get; set; }
    public decimal LucroBrl { get; set; }
    public decimal MargemPercent { get; set; }

    public string? CupomCodigo { get; set; }
    public TipoCupom? CupomTipo { get; set; }

    // Só vendas manuais (lançadas aqui): de onde veio o custo e o que foi usado no cálculo.
    public OrigemCusto? OrigemCusto { get; set; }
    public decimal? QuantidadeEsmeraldas { get; set; }
    public bool UsaTaxaPrata { get; set; }
    public decimal? DolarCompraUsado { get; set; }
    public string? Observacao { get; set; }
    public Guid? CriadoPor { get; set; }

    /// <summary>Última atualização na origem; regra de upsert: evento mais antigo que isto é ignorado.</summary>
    public DateTime AtualizadoEmOrigem { get; set; }
    public DateTime? RemovidaEm { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public List<VendaItem> Itens { get; set; } = [];

    public bool Removida => RemovidaEm is not null;
    public bool Manual => Origem == OrigemVenda.Manual;

    /// <summary>Entra no faturamento? Só vendas pagas ou concluídas e não removidas. Pendente ainda não foi recebida; cancelada e reembolsada não valem.</summary>
    public bool Faturavel => !Removida && StatusNormalizado is StatusVenda.Pago or StatusVenda.Concluido;
}

public class VendaItem : Entity
{
    public Guid VendaId { get; set; }
    public int Ordem { get; set; }
    public required string Descricao { get; set; }
    public decimal Quantidade { get; set; }
    public decimal PrecoUsd { get; set; }
    public decimal PrecoBrl { get; set; }
    public string? Referencia { get; set; }
}

/// <summary>Regras puras de upsert vindas do crivonft.</summary>
public static class RegraUpsert
{
    /// <summary>Aplica o evento? Só ignora quando é estritamente mais antigo que o já gravado (igual reaplica, é idempotente).</summary>
    public static bool Aplicar(DateTime? gravadoAtualizadoEm, DateTime recebidoAtualizadoEm) =>
        gravadoAtualizadoEm is null || recebidoAtualizadoEm >= gravadoAtualizadoEm;
}

/// <summary>Filtros reutilizáveis em consultas EF (a propriedade <see cref="Venda.Faturavel"/> não traduz para SQL).</summary>
public static class VendaConsultas
{
    /// <summary>Mesma regra de <see cref="Venda.Faturavel"/>, em forma de expressão.</summary>
    public static readonly System.Linq.Expressions.Expression<Func<Venda, bool>> Faturavel =
        v => v.RemovidaEm == null && (v.StatusNormalizado == StatusVenda.Pago || v.StatusNormalizado == StatusVenda.Concluido);
}
