using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Estoque;

public enum StatusItem { EmEstoque = 1, Vendido = 2, Cancelado = 3 }

/// <summary>Um item (NFT) comprado para revenda. O custo é o que foi pago por unidade, em reais; o preço de mercado vem do catálogo do crivonft.
/// Vender fecha o registro inteiro com o valor recebido; cancelar só vale enquanto está em estoque.</summary>
public class ItemEstoque : Entity
{
    public string? Categoria { get; set; }
    public string Nome { get; set; } = "";
    public string? ImagemUrl { get; set; }
    public int Quantidade { get; set; } = 1;
    public decimal CustoUnitario { get; set; }
    public DateOnly DataCompra { get; set; }
    public decimal? PrecoMercadoBrl { get; set; }
    public DateTime? PrecoAtualizadoEm { get; set; }
    public string? Observacao { get; set; }
    public StatusItem Status { get; set; } = StatusItem.EmEstoque;
    /// <summary>Total recebido na venda (todas as unidades).</summary>
    public decimal? ValorVenda { get; set; }
    public DateOnly? DataVenda { get; set; }
    public string? MotivoCancelamento { get; set; }
    public Guid? CriadoPor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public decimal CustoTotal => CustoUnitario * Quantidade;

    public void Vender(decimal valorTotal, DateOnly data)
    {
        if (Status != StatusItem.EmEstoque) throw new DomainException("item_nao_esta_em_estoque", "Este item não está mais em estoque.");
        if (valorTotal < 0) throw new DomainException("valor_invalido", "O valor da venda não pode ser negativo.");
        Status = StatusItem.Vendido;
        ValorVenda = valorTotal;
        DataVenda = data;
    }

    public void Cancelar(string? motivo)
    {
        if (Status != StatusItem.EmEstoque) throw new DomainException("item_nao_esta_em_estoque", "Só dá para cancelar um item que ainda está em estoque.");
        Status = StatusItem.Cancelado;
        MotivoCancelamento = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
    }
}
