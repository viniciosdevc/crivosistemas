using Crivosistemas.Domain.Estoque;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Precificacao;
using Crivosistemas.Domain.Vendas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crivosistemas.Infrastructure.Persistence.Configurations;

internal sealed class ClienteConfig : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> b)
    {
        // A collation padrão do MySQL 8 (utf8mb4_0900_ai_ci) não diferencia maiúsculas: "Ana" e "ana" são o mesmo cliente.
        b.Property(x => x.Chave).HasMaxLength(120);
        b.Property(x => x.Nickname).HasMaxLength(120);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.IdentidadeId).HasMaxLength(120);
        b.HasIndex(x => x.Chave).IsUnique();
        b.Property(x => x.Carteira).HasMaxLength(42);
        b.HasIndex(x => x.Nickname);
        b.HasIndex(x => x.Carteira);
    }
}

internal sealed class VendaConfig : IEntityTypeConfiguration<Venda>
{
    public void Configure(EntityTypeBuilder<Venda> b)
    {
        b.Property(x => x.Origem).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.OrigemId).HasMaxLength(64);
        b.Property(x => x.PedidoId).HasMaxLength(64);
        b.Property(x => x.Status).HasMaxLength(60);
        b.Property(x => x.StatusNormalizado).HasConversion<string>().HasMaxLength(15);
        b.Property(x => x.FormaPagamento).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Vendedor).HasMaxLength(120);
        b.Property(x => x.CupomCodigo).HasMaxLength(60);
        b.Property(x => x.CupomTipo).HasConversion<string>().HasMaxLength(15);
        foreach (var p in new[] { nameof(Venda.TotalBrl), nameof(Venda.DescontoBrl), nameof(Venda.TaxaBrl), nameof(Venda.ValorPixBrl),
                     nameof(Venda.CustoBrl), nameof(Venda.LucroBrl) })
            b.Property(p).HasPrecision(14, 2);
        b.Property(x => x.MargemPercent).HasPrecision(9, 2);
        b.Property(x => x.CustoUsd).HasPrecision(18, 8);
        b.Property(x => x.ValorCambios).HasPrecision(18, 4);
        b.Property(x => x.CustoCambios).HasPrecision(18, 4);
        b.Property(x => x.OrigemCusto).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.QuantidadeEsmeraldas).HasPrecision(18, 4);
        b.Property(x => x.DolarCompraUsado).HasPrecision(18, 8);
        b.Property(x => x.Observacao).HasMaxLength(500);
        b.Ignore(x => x.Manual);
        b.Ignore(x => x.Removida);
        b.Ignore(x => x.Faturavel);
        b.HasIndex(x => new { x.Origem, x.OrigemId }).IsUnique();
        b.HasIndex(x => x.DataHora);
        b.HasIndex(x => new { x.StatusNormalizado, x.DataHora });
        b.HasIndex(x => new { x.ClienteId, x.DataHora });
        b.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.Itens).WithOne().HasForeignKey(i => i.VendaId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VendaItemConfig : IEntityTypeConfiguration<VendaItem>
{
    public void Configure(EntityTypeBuilder<VendaItem> b)
    {
        b.Property(x => x.Descricao).HasMaxLength(300);
        b.Property(x => x.Referencia).HasMaxLength(120);
        b.Property(x => x.Quantidade).HasPrecision(18, 4);
        b.Property(x => x.PrecoUsd).HasPrecision(18, 8);
        b.Property(x => x.PrecoBrl).HasPrecision(14, 2);
        b.HasIndex(x => x.VendaId);
    }
}

internal sealed class EventoRecebidoConfig : IEntityTypeConfiguration<EventoRecebido>
{
    public void Configure(EntityTypeBuilder<EventoRecebido> b)
    {
        b.Property(x => x.Tipo).HasMaxLength(30);
        b.Property(x => x.Estado).HasConversion<string>().HasMaxLength(15);
        b.Property(x => x.Corpo).HasColumnType("longtext");
        b.Property(x => x.Erro).HasMaxLength(2000);
        b.Property(x => x.Resultado).HasMaxLength(200);
        b.Property(x => x.Origem).HasMaxLength(10);
        b.Property(x => x.OrigemId).HasMaxLength(64);
        b.Property(x => x.ClienteChave).HasMaxLength(120);
        b.HasIndex(x => x.EventoId).IsUnique();
        b.HasIndex(x => new { x.Estado, x.RecebidoEm });
        b.HasIndex(x => new { x.Origem, x.OrigemId });
        b.HasIndex(x => x.RecebidoEm);
    }
}

internal sealed class ReconciliacaoConfig : IEntityTypeConfiguration<ReconciliacaoExecucao>
{
    public void Configure(EntityTypeBuilder<ReconciliacaoExecucao> b)
    {
        b.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(15);
        b.Property(x => x.Estado).HasConversion<string>().HasMaxLength(15);
        b.Property(x => x.Erro).HasMaxLength(2000);
        b.Property(x => x.Aviso).HasMaxLength(500);
        b.HasIndex(x => new { x.Estado, x.CriadoEm });
        b.HasIndex(x => new { x.Tipo, x.CriadoEm });
    }
}

internal sealed class PrecificacaoConfigConfig : IEntityTypeConfiguration<PrecificacaoConfig>
{
    public void Configure(EntityTypeBuilder<PrecificacaoConfig> b)
    {
        foreach (var p in new[] { nameof(PrecificacaoConfig.ValorKFeira), nameof(PrecificacaoConfig.ValorKTrocas), nameof(PrecificacaoConfig.MoedasPorK),
                     nameof(PrecificacaoConfig.DolarPadraoCompra), nameof(PrecificacaoConfig.DolarPadraoVenda), nameof(PrecificacaoConfig.TaxaPrata),
                     nameof(PrecificacaoConfig.MargemVendaCambiosPercent) })
            b.Property(p).HasPrecision(18, 6);
    }
}

internal sealed class FaixaPixConfig : IEntityTypeConfiguration<FaixaPix>
{
    public void Configure(EntityTypeBuilder<FaixaPix> b)
    {
        b.Property(x => x.ValorMinUsd).HasPrecision(18, 6);
        b.Property(x => x.ValorMaxUsd).HasPrecision(18, 6);
        b.Property(x => x.Multiplicador).HasPrecision(18, 6);
        b.HasIndex(x => x.Ordem);
    }
}

internal sealed class PrecificacaoHistoricoConfig : IEntityTypeConfiguration<PrecificacaoHistorico>
{
    public void Configure(EntityTypeBuilder<PrecificacaoHistorico> b)
    {
        b.Property(x => x.UserNome).HasMaxLength(120);
        b.Property(x => x.Origem).HasMaxLength(30);
        b.Property(x => x.Antes).HasColumnType("json");
        b.Property(x => x.Depois).HasColumnType("json");
        b.HasIndex(x => x.Versao);
    }
}

internal sealed class PrecificacaoEnvioConfig : IEntityTypeConfiguration<PrecificacaoEnvio>
{
    public void Configure(EntityTypeBuilder<PrecificacaoEnvio> b)
    {
        b.Property(x => x.Payload).HasColumnType("json");
        b.Property(x => x.Estado).HasConversion<string>().HasMaxLength(15);
        b.Property(x => x.UltimoErro).HasMaxLength(2000);
        b.HasIndex(x => new { x.Estado, x.ProximaTentativaEm });
        b.HasIndex(x => x.Versao);
    }
}

internal sealed class LoteEstoqueConfig : IEntityTypeConfiguration<LoteEstoque>
{
    public void Configure(EntityTypeBuilder<LoteEstoque> b)
    {
        b.ToTable("LotesEstoque");
        b.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Origem).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.OrigemId).HasMaxLength(64);
        b.Property(x => x.Quantidade).HasPrecision(18, 8);
        b.Property(x => x.Saldo).HasPrecision(18, 8);
        b.Property(x => x.CustoTotal).HasPrecision(18, 8);
        b.Property(x => x.CustoUnitario).HasPrecision(18, 8);
        b.Property(x => x.Observacao).HasMaxLength(300);
        b.Property(x => x.MotivoCancelamento).HasMaxLength(300);
        b.Ignore(x => x.Cancelado);
        b.Ignore(x => x.Intocado);
        b.HasIndex(x => new { x.Tipo, x.DataCompra, x.CriadoEm });
        b.HasIndex(x => new { x.Tipo, x.OrigemId }).IsUnique();
    }
}

internal sealed class MovimentoEstoqueConfig : IEntityTypeConfiguration<MovimentoEstoque>
{
    public void Configure(EntityTypeBuilder<MovimentoEstoque> b)
    {
        b.ToTable("MovimentosEstoque");
        b.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Motivo).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Quantidade).HasPrecision(18, 8);
        b.Property(x => x.CustoUnitario).HasPrecision(18, 8);
        b.Property(x => x.Observacao).HasMaxLength(300);
        b.HasOne<LoteEstoque>().WithMany().HasForeignKey(x => x.LoteId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Tipo, x.CriadoEm });
        b.HasIndex(x => x.VendaId);
    }
}

internal sealed class ConfiguracaoEstoqueConfig : IEntityTypeConfiguration<ConfiguracaoEstoque>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoEstoque> b)
    {
        b.ToTable("ConfiguracoesEstoque");
        b.Property(x => x.CustoMedioUsdFixo).HasPrecision(18, 8);
    }
}
