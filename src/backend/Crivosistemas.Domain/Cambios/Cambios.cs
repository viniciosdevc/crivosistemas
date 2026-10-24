using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Cambios;

/// <summary>Por que o saldo de câmbios mudou. Venda = câmbios vendidos por R$ (sai saldo, entra dinheiro); VendaManual = câmbios recebidos
/// numa venda lançada aqui (entra saldo); Ajuste = saldo definido à mão; Estorno desfaz um dos anteriores.</summary>
public enum TipoMovimentoCambios { Venda = 1, VendaManual = 2, Ajuste = 3, Estorno = 4 }

/// <summary>Saldo atual de câmbios (singleton). A linha também serve de trava (SELECT … FOR UPDATE) para as escritas.</summary>
public class SaldoCambios : Entity
{
    public decimal Saldo { get; set; }
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Livro do saldo: cada linha guarda o saldo antes e depois. Quantidade positiva entra, negativa sai.
/// <see cref="ValorBrl"/> só existe na venda de câmbios (é o que entra no financeiro). Nada é apagado: desfazer gera um Estorno.</summary>
public class MovimentoCambios : Entity
{
    public TipoMovimentoCambios Tipo { get; set; }
    public decimal Quantidade { get; set; }
    public decimal SaldoAnterior { get; set; }
    public decimal SaldoNovo { get; set; }
    public decimal? ValorBrl { get; set; }
    public Guid? ClienteId { get; set; }
    /// <summary>Venda manual que gerou o saldo (VendaManual) ou movimento original (Estorno em <see cref="EstornoDeId"/>).</summary>
    public Guid? VendaId { get; set; }
    public Guid? EstornoDeId { get; set; }
    public DateTime? EstornadoEm { get; set; }
    public string? Observacao { get; set; }
    public Guid? UserId { get; set; }
    public DateTime DataHora { get; set; } = DateTime.UtcNow;
}
