using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Estoque;

/// <summary>O que o lote guarda: dólares (custo em R$ por US$) ou esmeraldas (custo em US$ por esmeralda).</summary>
public enum TipoEstoque { Usd = 1, Esmeralda = 2 }

/// <summary>Como o lote nasceu.</summary>
public enum OrigemLote { Compra = 1, Ajuste = 2, ImportacaoCrivonft = 3 }

public enum MotivoMovimento { Compra = 1, Venda = 2, EstornoVenda = 3, Ajuste = 4, Cancelamento = 5, Importacao = 6, Edicao = 7 }

/// <summary>Um lote de compra. O saldo cai com vendas e ajustes; o custo unitário não muda depois de consumido.
/// Para USD: <see cref="Quantidade"/> é US$ recebido, <see cref="CustoTotal"/> é R$ depositado e <see cref="CustoUnitario"/> é R$/US$.
/// Para esmeraldas: quantidade de esmeraldas, custo total em US$ e US$/esmeralda.</summary>
public class LoteEstoque : Entity
{
    public TipoEstoque Tipo { get; set; }
    public OrigemLote Origem { get; set; }
    /// <summary>Id do lote no crivonft quando importado (chave de idempotência).</summary>
    public string? OrigemId { get; set; }
    public DateOnly DataCompra { get; set; }
    public decimal Quantidade { get; set; }
    public decimal Saldo { get; set; }
    public decimal CustoTotal { get; set; }
    public decimal CustoUnitario { get; set; }
    public string? Observacao { get; set; }
    public Guid? CriadoPor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? CanceladoEm { get; set; }
    public string? MotivoCancelamento { get; set; }

    public bool Cancelado => CanceladoEm is not null;
    /// <summary>Nada saiu deste lote ainda: só assim ele pode ser editado ou cancelado (outros registros podem depender do que saiu).</summary>
    public bool Intocado => Saldo == Quantidade;
}

/// <summary>Cada entrada ou saída de saldo, com o lote e o custo daquele momento. É o que permite estornar uma venda devolvendo ao mesmo lote.</summary>
public class MovimentoEstoque : Entity
{
    public TipoEstoque Tipo { get; set; }
    public Guid LoteId { get; set; }
    /// <summary>Positivo entra, negativo sai.</summary>
    public decimal Quantidade { get; set; }
    public decimal CustoUnitario { get; set; }
    public MotivoMovimento Motivo { get; set; }
    public Guid? VendaId { get; set; }
    /// <summary>Movimento de venda já devolvido (estorno feito).</summary>
    public DateTime? EstornadoEm { get; set; }
    public string? Observacao { get; set; }
    public Guid? UserId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Configuração do estoque (singleton).</summary>
public class ConfiguracaoEstoque : Entity
{
    /// <summary>Custo médio do dólar fixado à mão (R$/US$). Nulo = calculado pelos lotes.</summary>
    public decimal? CustoMedioUsdFixo { get; set; }
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

public sealed record SaldoLote(Guid Id, decimal Saldo, decimal CustoUnitario);
public sealed record Debito(Guid LoteId, decimal Quantidade, decimal CustoUnitario);

/// <summary>Regras puras do estoque, iguais às do crivonft (src/lib/inventory.ts).</summary>
public static class RegrasEstoque
{
    public const int CasasQuantidade = 8;

    /// <summary>Custo médio ponderado pelo saldo: Σ(custo × saldo) / Σ saldo. Sem saldo, 0.</summary>
    public static decimal CustoMedio(IEnumerable<SaldoLote> lotes)
    {
        decimal soma = 0, saldo = 0;
        foreach (var l in lotes.Where(l => l.Saldo > 0))
        {
            soma += l.CustoUnitario * l.Saldo;
            saldo += l.Saldo;
        }
        return saldo == 0 ? 0 : Math.Round(soma / saldo, CasasQuantidade, MidpointRounding.AwayFromZero);
    }

    public static decimal CustoUnitario(decimal custoTotal, decimal quantidade) =>
        quantidade > 0 ? Math.Round(custoTotal / quantidade, CasasQuantidade, MidpointRounding.AwayFromZero) : 0;

    /// <summary>Saída por FIFO (lotes na ordem recebida, do mais antigo). Falta saldo → 422.</summary>
    public static List<Debito> PlanoFifo(IReadOnlyList<SaldoLote> lotesMaisAntigosPrimeiro, decimal quantidade, string unidade)
    {
        ExigirSaldo(lotesMaisAntigosPrimeiro, quantidade, unidade);
        var plano = new List<Debito>();
        var resta = quantidade;
        foreach (var l in lotesMaisAntigosPrimeiro.Where(l => l.Saldo > 0))
        {
            if (resta <= 0) break;
            var d = Math.Min(l.Saldo, resta);
            plano.Add(new Debito(l.Id, d, l.CustoUnitario));
            resta -= d;
        }
        return plano;
    }

    /// <summary>Saída proporcional ao saldo de cada lote: o custo médio não se move (usado em ajustes manuais).</summary>
    public static List<Debito> PlanoProporcional(IReadOnlyList<SaldoLote> lotes, decimal quantidade, string unidade)
    {
        ExigirSaldo(lotes, quantidade, unidade);
        var comSaldo = lotes.Where(l => l.Saldo > 0).ToList();
        var total = comSaldo.Sum(l => l.Saldo);
        var plano = new List<Debito>();
        var resta = quantidade;
        for (var i = 0; i < comSaldo.Count && resta > 0; i++)
        {
            var l = comSaldo[i];
            var d = i == comSaldo.Count - 1
                ? resta
                : Math.Min(l.Saldo, Math.Round(l.Saldo / total * quantidade, CasasQuantidade, MidpointRounding.ToZero));
            d = Math.Min(d, l.Saldo);
            if (d <= 0) continue;
            plano.Add(new Debito(l.Id, d, l.CustoUnitario));
            resta -= d;
        }
        // Arredondamento deixou sobra (o último lote não comportou): completa por ordem.
        foreach (var l in comSaldo)
        {
            if (resta <= 0) break;
            var usado = plano.Where(p => p.LoteId == l.Id).Sum(p => p.Quantidade);
            var livre = l.Saldo - usado;
            if (livre <= 0) continue;
            var d = Math.Min(livre, resta);
            plano.Add(new Debito(l.Id, d, l.CustoUnitario));
            resta -= d;
        }
        return plano;
    }

    private static void ExigirSaldo(IReadOnlyList<SaldoLote> lotes, decimal quantidade, string unidade)
    {
        if (quantidade <= 0) throw new DomainException("quantidade_invalida", "Informe uma quantidade maior que zero.");
        var saldo = lotes.Where(l => l.Saldo > 0).Sum(l => l.Saldo);
        if (quantidade > saldo)
            throw new DomainException("saldo_insuficiente", $"Saldo insuficiente de {unidade}. Disponível: {saldo:0.##}, pedido: {quantidade:0.##}.");
    }
}
