namespace Crivosistemas.Domain.Vendas;

public enum SegmentoCliente { Vip, Recorrente, Novo, Ocasional, EmRisco, Inativo, NuncaComprou }

/// <summary>Resumo de compras de um cliente (só vendas faturáveis).</summary>
public sealed record ClienteCompras(Guid Id, string Nickname, bool Favorito, decimal TotalGasto, int Compras, DateTime? PrimeiraCompra, DateTime? UltimaCompra);

public sealed record ClienteCrm(Guid Id, string Nickname, bool Favorito, decimal TotalGasto, int Compras, decimal TicketMedio, DateTime? PrimeiraCompra, DateTime? UltimaCompra, int? DiasSemComprar, SegmentoCliente Segmento, bool Vip);

public sealed record SegmentoResumo(SegmentoCliente Segmento, int Clientes, decimal Receita);

public sealed record TotaisCrm(int Clientes, int Compradores, int NuncaComprou, decimal Receita, decimal TicketMedio, decimal ComprasPorCliente, decimal LtvMedio, decimal DiasMedioDesdeUltima, decimal TaxaRecompra, decimal ConcentracaoTop10);

public sealed record AnaliseCrm(TotaisCrm Totais, IReadOnlyList<SegmentoResumo> Segmentos, IReadOnlyList<ClienteCrm> Top, IReadOnlyList<ClienteCrm> Reativar);

/// <summary>Segmentação de clientes (CRM). Regras puras, sem banco: o endpoint só alimenta com os totais por cliente.</summary>
public static class CrmClientes
{
    public const int DiasNovoPadrao = 30;
    public const int TopTamanho = 10;

    /// <param name="diasInativo">Sem comprar há mais que isso = em risco; mais que o dobro = inativo.</param>
    public static AnaliseCrm Analisar(IReadOnlyCollection<ClienteCompras> clientes, DateTime agora, int diasInativo, int limiteReativar = 50)
    {
        var compradores = clientes.Where(c => c.Compras > 0 && c.UltimaCompra is not null).OrderByDescending(c => c.TotalGasto).ToList();
        // VIP: os 10% que mais gastaram (ao menos 1), desde que tenham gasto algo.
        var vipCorte = compradores.Count == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(compradores.Count * 0.10m));
        var vips = compradores.Take(vipCorte).Where(c => c.TotalGasto > 0).Select(c => c.Id).ToHashSet();

        var linhas = clientes.Select(c => Classificar(c, agora, diasInativo, vips.Contains(c.Id))).ToList();
        var comprou = linhas.Where(l => l.Compras > 0).ToList();
        var receita = comprou.Sum(l => l.TotalGasto);
        var top = comprou.OrderByDescending(l => l.TotalGasto).ThenBy(l => l.Nickname).ToList();
        var totalCompras = comprou.Sum(l => l.Compras);

        var totais = new TotaisCrm(
            clientes.Count, comprou.Count, clientes.Count - comprou.Count, receita,
            totalCompras == 0 ? 0 : receita / totalCompras,
            comprou.Count == 0 ? 0 : (decimal)totalCompras / comprou.Count,
            comprou.Count == 0 ? 0 : receita / comprou.Count,
            comprou.Count == 0 ? 0 : (decimal)comprou.Average(l => l.DiasSemComprar ?? 0),
            comprou.Count == 0 ? 0 : 100m * comprou.Count(l => l.Compras >= 2) / comprou.Count,
            receita == 0 ? 0 : 100m * top.Take(TopTamanho).Sum(l => l.TotalGasto) / receita);

        var segmentos = Enum.GetValues<SegmentoCliente>()
            .Select(s => new SegmentoResumo(s, linhas.Count(l => l.Segmento == s), linhas.Where(l => l.Segmento == s).Sum(l => l.TotalGasto)))
            .ToList();

        // Reativação: quem já comprou e parou. Quem mais gastou (e favoritos) primeiro.
        var reativar = linhas.Where(l => l.Segmento is SegmentoCliente.EmRisco or SegmentoCliente.Inativo)
            .OrderByDescending(l => l.Favorito).ThenByDescending(l => l.TotalGasto).Take(limiteReativar).ToList();
        return new AnaliseCrm(totais, segmentos, top.Take(TopTamanho).ToList(), reativar);
    }

    public static ClienteCrm Classificar(ClienteCompras c, DateTime agora, int diasInativo, bool vip)
    {
        var dias = c.UltimaCompra is { } u ? Math.Max(0, (int)(agora - u).TotalDays) : (int?)null;
        var ticket = c.Compras == 0 ? 0 : c.TotalGasto / c.Compras;
        SegmentoCliente seg;
        if (c.Compras == 0 || dias is null) seg = SegmentoCliente.NuncaComprou;
        else if (dias > diasInativo * 2) seg = SegmentoCliente.Inativo;
        else if (dias > diasInativo) seg = SegmentoCliente.EmRisco;
        else if (vip) seg = SegmentoCliente.Vip;
        else if (c.PrimeiraCompra is { } p && (agora - p).TotalDays <= DiasNovoPadrao) seg = SegmentoCliente.Novo;
        else if (c.Compras >= 2) seg = SegmentoCliente.Recorrente;
        else seg = SegmentoCliente.Ocasional;
        return new ClienteCrm(c.Id, c.Nickname, c.Favorito, c.TotalGasto, c.Compras, ticket, c.PrimeiraCompra, c.UltimaCompra, dias, seg, vip);
    }
}
