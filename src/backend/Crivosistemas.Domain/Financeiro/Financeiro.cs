using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Financeiro;

public enum TipoLancamento { Entrada = 1, Saida = 2 }

/// <summary>Pendente ainda não caiu/saiu da conta; só confirmado entra no saldo. Cancelado fica no histórico.</summary>
public enum StatusLancamento { Pendente = 1, Confirmado = 2, Cancelado = 3 }

/// <summary>De onde veio o lançamento. Os automáticos acompanham a origem (venda, compra de dólar) e não são mexidos à mão.</summary>
public enum OrigemLancamento { Manual = 1, Venda = 2, CompraUsd = 3 }

/// <summary>Uma entrada ou saída de dinheiro (R$), como o livro-razão <c>financeiro_lancamentos</c> do crivonft.</summary>
public class LancamentoFinanceiro : Entity
{
    public TipoLancamento Tipo { get; set; }
    public StatusLancamento Status { get; set; }
    public OrigemLancamento Origem { get; set; }
    public required string Categoria { get; set; }
    public string? Descricao { get; set; }
    public decimal ValorBrl { get; set; }
    /// <summary>Dia de calendário (SP) em que o dinheiro entrou ou saiu (ou vai entrar/sair, se pendente).</summary>
    public DateOnly Data { get; set; }
    /// <summary>Venda que gerou a entrada do PIX (no máximo um lançamento por venda).</summary>
    public Guid? VendaId { get; set; }
    /// <summary>Compra de dólar (lote do estoque) que gerou a saída (no máximo um lançamento por lote).</summary>
    public Guid? LoteEstoqueId { get; set; }
    public string? MotivoCancelamento { get; set; }
    public Guid? CriadoPor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmadoEm { get; set; }
    public DateTime? CanceladoEm { get; set; }

    public bool Automatico => Origem != OrigemLancamento.Manual;

    /// <summary>Valor com sinal: entrada soma, saída subtrai.</summary>
    public decimal ValorComSinal => Tipo == TipoLancamento.Entrada ? ValorBrl : -ValorBrl;

    public void Confirmar(DateTime agora)
    {
        if (Automatico) throw new DomainException("lancamento_automatico", "Este lançamento acompanha a venda ou a compra de origem e não muda à mão.");
        if (Status != StatusLancamento.Pendente) throw new DomainException("status_invalido", "Só um lançamento pendente pode ser confirmado.");
        Status = StatusLancamento.Confirmado;
        ConfirmadoEm = AtualizadoEm = agora;
    }

    /// <summary>Estorna um lançamento manual: ele continua no histórico, fora do saldo. Pagamentos não são editados nem apagados.</summary>
    public void Cancelar(string? motivo, DateTime agora)
    {
        if (Automatico)
            throw new DomainException("lancamento_automatico", "Este lançamento acompanha a venda ou a compra de origem. Cancele a venda ou a compra por lá.");
        if (Status == StatusLancamento.Cancelado) throw new DomainException("status_invalido", "Este lançamento já está cancelado.");
        var m = motivo?.Trim();
        if (string.IsNullOrEmpty(m)) throw new DomainException("motivo_obrigatorio", "Informe o motivo do cancelamento.");
        Status = StatusLancamento.Cancelado;
        MotivoCancelamento = m.Length > 300 ? m[..300] : m;
        CanceladoEm = AtualizadoEm = agora;
    }
}

public static class RegrasFinanceiro
{
    public const string CategoriaVendaPix = "Venda PIX";
    public const string CategoriaCompraUsd = "Compra de USD";

    /// <summary>Sugestões do formulário (as mesmas do crivonft, mais as que já foram usadas).</summary>
    public static readonly string[] CategoriasSugeridas =
        [CategoriaVendaPix, CategoriaCompraUsd, "Aporte", "Retirada", "Salário", "Comissão", "Anúncios", "Ferramentas", "Taxas", "Impostos", "Reembolso", "Outros"];

    public static string Tipo(TipoLancamento t) => t == TipoLancamento.Entrada ? "entrada" : "saida";
    public static bool TentarTipo(string? s, out TipoLancamento t)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "entrada": t = TipoLancamento.Entrada; return true;
            case "saida" or "saída": t = TipoLancamento.Saida; return true;
            default: t = default; return false;
        }
    }

    public static string Status(StatusLancamento s) => s switch
    {
        StatusLancamento.Pendente => "pendente", StatusLancamento.Confirmado => "confirmado", _ => "cancelado",
    };
    public static bool TentarStatus(string? s, out StatusLancamento v)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "pendente": v = StatusLancamento.Pendente; return true;
            case "confirmado": v = StatusLancamento.Confirmado; return true;
            case "cancelado": v = StatusLancamento.Cancelado; return true;
            default: v = default; return false;
        }
    }

    public static string Origem(OrigemLancamento o) => o switch
    {
        OrigemLancamento.Venda => "venda", OrigemLancamento.CompraUsd => "compra_usd", _ => "manual",
    };

    /// <summary>"Venda PIX: Mobi raro → nick" (como no crivonft), cortado em 300.</summary>
    public static string DescricaoVendaPix(string? item, string? nickname)
    {
        var d = $"Venda PIX: {(string.IsNullOrWhiteSpace(item) ? "venda" : item.Trim())}{(string.IsNullOrWhiteSpace(nickname) ? "" : $" → {nickname.Trim()}")}";
        return d.Length > 300 ? d[..300] : d;
    }

    public static string DescricaoCompraUsd(decimal usdRecebido) =>
        $"Depósito USD: ${usdRecebido.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} recebidos";
}
