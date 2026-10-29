using Crivosistemas.Domain.Estoque;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Infrastructure.Estoque;

public sealed record LinhaRecalculo(Guid VendaId, string Origem, DateTime DataHora, string Item, decimal TotalBrl, decimal CustoUsd,
    decimal CustoAntes, decimal LucroAntes, decimal CustoDepois, decimal LucroDepois, decimal FaltouUsd, bool JaBaixada);

public sealed record ResultadoRecalculo(DateOnly Desde, bool Aplicado, int Vendas, decimal LucroAntes, decimal LucroDepois, decimal FaltouUsd, IReadOnlyList<LinhaRecalculo> Linhas);

/// <summary>Refaz o custo e a baixa de dólar das vendas passadas na ordem em que aconteceram, por FIFO sobre os lotes do Gestão:
/// devolve aos lotes o que cada venda do período tirou e baixa de novo, venda a venda, do lote mais antigo disponível naquele dia.
/// Rodar de novo dá o mesmo resultado (nada é baixado em dobro). Vendas anteriores a "desde" não são tocadas.
/// A prévia (<c>aplicar</c> falso) só calcula; aplicar exige transação e o estoque travado por quem chama.</summary>
public sealed class RecalculoCustoService(AppDbContext db, EstoqueService estoque)
{
    public async Task<ResultadoRecalculo> ExecutarAsync(DateOnly desde, bool aplicar, Guid? userId, CancellationToken ct)
    {
        var inicio = FusoSp.InicioDoDiaUtc(desde);
        var consulta = db.Vendas.Where(v => v.DataHora >= inicio && v.RemovidaEm == null && v.CustoUsd > 0
            && (v.StatusNormalizado == StatusVenda.Pago || v.StatusNormalizado == StatusVenda.Concluido)
            && (v.Origem == OrigemVenda.Nft || v.Origem == OrigemVenda.Site || (v.Origem == OrigemVenda.Manual && v.OrigemCusto == OrigemCusto.EstoqueUsd)));
        // Na prévia nada é gravado: sem rastreamento, o que se lê não vira alteração.
        var vendas = await (aplicar ? consulta : consulta.AsNoTracking()).Include(v => v.Itens)
            .OrderBy(v => v.DataHora).ThenBy(v => v.Id).ToListAsync(ct);
        var ids = vendas.Select(v => v.Id).ToList();

        if (aplicar)
        {
            await estoque.TravarAsync(ct);
            foreach (var v in vendas) await estoque.EstornarVendaAsync(v.Id, TipoEstoque.Usd, userId, ct);
        }

        var lotes = await db.LotesEstoque.AsNoTracking().Where(l => l.Tipo == TipoEstoque.Usd && l.CanceladoEm == null)
            .OrderBy(l => l.DataCompra).ThenBy(l => l.CriadoEm).ThenBy(l => l.Id).ToListAsync(ct);
        var sim = lotes.Select(l => new LoteSimulado(l.Id, l.DataCompra, l.Saldo, l.CustoUnitario)).ToList();
        if (!aplicar)
        {
            // Prévia: devolve em memória o que essas vendas já tiraram, como se o estorno tivesse acontecido.
            var movs = await db.MovimentosEstoque.AsNoTracking()
                .Where(m => m.Tipo == TipoEstoque.Usd && m.Motivo == MotivoMovimento.Venda && m.EstornadoEm == null && m.VendaId != null && ids.Contains(m.VendaId.Value))
                .ToListAsync(ct);
            var porLote = sim.ToDictionary(l => l.Id);
            foreach (var m in movs) if (porLote.TryGetValue(m.LoteId, out var l)) l.Saldo += -m.Quantidade;
        }
        var baixadas = (await db.MovimentosEstoque.AsNoTracking()
            .Where(m => m.Tipo == TipoEstoque.Usd && m.Motivo == MotivoMovimento.Venda && m.VendaId != null && ids.Contains(m.VendaId.Value))
            .Select(m => m.VendaId!.Value).Distinct().ToListAsync(ct)).ToHashSet();

        var medio = RegrasEstoque.CustoMedio(sim.Select(l => new SaldoLote(l.Id, l.Saldo, l.CustoUnitario)).ToList());
        var linhas = new List<LinhaRecalculo>();
        foreach (var v in vendas)
        {
            var (plano, faltou) = RegrasEstoque.PlanoMedioAteData(sim, v.CustoUsd, FusoSp.Dia(v.DataHora));
            var custoLotes = plano.Sum(d => d.Quantidade * d.CustoUnitario);
            var custoFifo = custoLotes + faltou * medio;
            var custoAntes = v.CustoBrl;
            var lucroAntes = v.LucroBrl;
            decimal custoDepois, lucroDepois;
            if (v.Origem == OrigemVenda.Manual)
            {
                // Custo da venda manual traz a taxa de prata e o resto da calculadora: troca só a parte do dólar (estimada pelo dólar usado → custo dos lotes).
                var estimado = v.CustoUsd * (v.DolarCompraUsado ?? 0);
                custoDepois = v.DolarCompraUsado is > 0 ? Math.Round(v.CustoBrl + custoFifo - estimado, 2, MidpointRounding.AwayFromZero) : v.CustoBrl;
                lucroDepois = v.TotalBrl - custoDepois;
            }
            else
            {
                (custoDepois, lucroDepois, _) = CustoVendaSite.ComCusto(v.TotalBrl, custoFifo + v.TaxaPrataBrl);
            }

            if (aplicar)
            {
                await estoque.AplicarPlanoVendaAsync(plano, v.Id, userId, ct);
                var margem = v.Origem == OrigemVenda.Manual
                    ? (custoDepois > 0 ? Math.Round(lucroDepois / custoDepois * 100, 2, MidpointRounding.AwayFromZero) : 0)
                    : CustoVendaSite.ComCusto(v.TotalBrl, custoFifo + v.TaxaPrataBrl).MargemPercent;
                v.CustoBrl = custoDepois;
                v.LucroBrl = lucroDepois;
                v.MargemPercent = margem;
                if (v.Origem != OrigemVenda.Manual) v.OrigemCusto = OrigemCusto.EstoqueUsd;
                if (v.Origem != OrigemVenda.Manual || v.DolarCompraUsado is > 0)
                    v.DolarCompraUsado = Math.Round(custoFifo / v.CustoUsd, 4, MidpointRounding.AwayFromZero);
                if (v.Observacao?.StartsWith(CustoVendaSite.AvisoFalta, StringComparison.Ordinal) == true) v.Observacao = null;
                if (faltou > 0) v.Observacao = $"{CustoVendaSite.AvisoFalta}faltaram US$ {faltou:0.##} para baixar esta venda.";
                v.AtualizadoEm = DateTime.UtcNow;
            }

            linhas.Add(new LinhaRecalculo(v.Id, Nomes.Origem(v.Origem), v.DataHora, v.Itens.OrderBy(i => i.Ordem).FirstOrDefault()?.Descricao ?? "",
                v.TotalBrl, v.CustoUsd, custoAntes, lucroAntes, custoDepois, lucroDepois, faltou, baixadas.Contains(v.Id)));
        }
        if (aplicar) await db.SaveChangesAsync(ct);

        return new ResultadoRecalculo(desde, aplicar, linhas.Count, linhas.Sum(l => l.LucroAntes), linhas.Sum(l => l.LucroDepois), linhas.Sum(l => l.FaltouUsd), linhas);
    }
}
