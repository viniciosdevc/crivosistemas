using System.Globalization;
using System.Text.Json;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Estoque;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Infrastructure.Estoque;

public sealed record ResumoEstoque(string Tipo, decimal Saldo, decimal CustoMedio, decimal CustoMedioCalculado, decimal? CustoMedioFixo, decimal ValorEmEstoque, int Lotes);

public sealed record ResultadoImportacaoEstoque(int UsdImportados, int EsmeraldasImportados, int Ignorados);

/// <summary>Estoque de dólar e de esmeraldas em lotes, com custo médio ponderado (mesmas regras do crivonft, src/lib/inventory.ts).
/// Toda escrita deve rodar dentro de uma transação (InTransactionAsync) e chamar <see cref="TravarAsync"/> antes de ler saldos:
/// a linha de configuração funciona como trava, então duas saídas simultâneas nunca gastam o mesmo saldo.</summary>
public sealed class EstoqueService(AppDbContext db, ICrivonftClient crivonft)
{
    public static string Nome(TipoEstoque t) => t == TipoEstoque.Usd ? "usd" : "esmeraldas";
    public static bool TentarTipo(string? s, out TipoEstoque t)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "usd": t = TipoEstoque.Usd; return true;
            case "esmeralda" or "esmeraldas": t = TipoEstoque.Esmeralda; return true;
            default: t = default; return false;
        }
    }
    private static string Unidade(TipoEstoque t) => t == TipoEstoque.Usd ? "dólar (US$)" : "esmeraldas";

    private async Task<ConfiguracaoEstoque> ConfigAsync(CancellationToken ct)
    {
        var c = await db.ConfiguracoesEstoque.FirstOrDefaultAsync(ct);
        if (c is not null) return c;
        c = new ConfiguracaoEstoque();
        db.ConfiguracoesEstoque.Add(c);
        await db.SaveChangesAsync(ct);
        return c;
    }

    /// <summary>Trava a linha de configuração até o fim da transação (SELECT … FOR UPDATE).</summary>
    public async Task TravarAsync(CancellationToken ct)
    {
        var c = await ConfigAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM ConfiguracoesEstoque WHERE Id = {c.Id} FOR UPDATE", ct);
    }

    private Task<List<LoteEstoque>> LotesComSaldoAsync(TipoEstoque tipo, CancellationToken ct) =>
        db.LotesEstoque.Where(l => l.Tipo == tipo && l.CanceladoEm == null && l.Saldo > 0)
            .OrderBy(l => l.DataCompra).ThenBy(l => l.CriadoEm).ThenBy(l => l.Id).ToListAsync(ct);

    public async Task<ResumoEstoque> ResumoAsync(TipoEstoque tipo, CancellationToken ct)
    {
        var lotes = await db.LotesEstoque.AsNoTracking().Where(l => l.Tipo == tipo && l.CanceladoEm == null && l.Saldo > 0)
            .Select(l => new SaldoLote(l.Id, l.Saldo, l.CustoUnitario)).ToListAsync(ct);
        var calculado = RegrasEstoque.CustoMedio(lotes);
        decimal? fixo = tipo == TipoEstoque.Usd ? (await db.ConfiguracoesEstoque.AsNoTracking().FirstOrDefaultAsync(ct))?.CustoMedioUsdFixo : null;
        return new ResumoEstoque(Nome(tipo), lotes.Sum(l => l.Saldo), fixo ?? calculado, calculado, fixo,
            Math.Round(lotes.Sum(l => l.Saldo * l.CustoUnitario), 2, MidpointRounding.AwayFromZero), lotes.Count);
    }

    private void Movimento(TipoEstoque tipo, Guid loteId, decimal qtd, decimal custo, MotivoMovimento motivo, Guid? userId, Guid? vendaId = null, string? obs = null) =>
        db.MovimentosEstoque.Add(new MovimentoEstoque
        {
            Tipo = tipo, LoteId = loteId, Quantidade = qtd, CustoUnitario = custo, Motivo = motivo, UserId = userId, VendaId = vendaId, Observacao = obs,
        });

    private static void ValidarCompra(decimal quantidade, decimal custoTotal)
    {
        if (quantidade <= 0) throw new DomainException("quantidade_invalida", "A quantidade precisa ser maior que zero.");
        if (custoTotal < 0) throw new DomainException("custo_invalido", "O custo não pode ser negativo.");
    }

    /// <summary>Compra de dólar (R$ depositado → US$ recebido) ou de esmeraldas (US$ pago → esmeraldas).</summary>
    public async Task<LoteEstoque> RegistrarCompraAsync(TipoEstoque tipo, DateOnly data, decimal quantidade, decimal custoTotal, string? obs, Guid userId, CancellationToken ct)
    {
        ValidarCompra(quantidade, custoTotal);
        var lote = new LoteEstoque
        {
            Tipo = tipo, Origem = OrigemLote.Compra, DataCompra = data, Quantidade = quantidade, Saldo = quantidade, CustoTotal = custoTotal,
            CustoUnitario = RegrasEstoque.CustoUnitario(custoTotal, quantidade), Observacao = Limpar(obs), CriadoPor = userId,
        };
        db.LotesEstoque.Add(lote);
        Movimento(tipo, lote.Id, quantidade, lote.CustoUnitario, MotivoMovimento.Compra, userId);
        await db.SaveChangesAsync(ct);
        return lote;
    }

    private async Task<LoteEstoque> LoteAsync(TipoEstoque tipo, Guid id, CancellationToken ct) =>
        await db.LotesEstoque.FirstOrDefaultAsync(l => l.Id == id && l.Tipo == tipo, ct)
        ?? throw new DomainException("lote_inexistente", "Lote não encontrado.");

    private static void ExigirIntocado(LoteEstoque l)
    {
        if (l.Cancelado) throw new DomainException("lote_cancelado", "Este lote já foi cancelado.");
        if (!l.Intocado)
            throw new DomainException("lote_consumido", "Já saiu saldo deste lote, então ele não pode ser editado nem cancelado. Use um ajuste de saldo.");
    }

    /// <summary>Corrige uma compra digitada errado. Só enquanto nada saiu dela.</summary>
    public async Task<LoteEstoque> EditarCompraAsync(TipoEstoque tipo, Guid id, DateOnly data, decimal quantidade, decimal custoTotal, string? obs, Guid userId, CancellationToken ct)
    {
        ValidarCompra(quantidade, custoTotal);
        var l = await LoteAsync(tipo, id, ct);
        ExigirIntocado(l);
        var delta = quantidade - l.Quantidade;
        l.DataCompra = data;
        l.Quantidade = quantidade;
        l.Saldo = quantidade;
        l.CustoTotal = custoTotal;
        l.CustoUnitario = RegrasEstoque.CustoUnitario(custoTotal, quantidade);
        l.Observacao = Limpar(obs);
        if (delta != 0) Movimento(tipo, l.Id, delta, l.CustoUnitario, MotivoMovimento.Edicao, userId);
        await db.SaveChangesAsync(ct);
        return l;
    }

    /// <summary>Cancela uma compra lançada por engano (fica no histórico, saldo zerado). Só enquanto nada saiu dela.</summary>
    public async Task<LoteEstoque> CancelarCompraAsync(TipoEstoque tipo, Guid id, string motivo, Guid userId, CancellationToken ct)
    {
        var l = await LoteAsync(tipo, id, ct);
        ExigirIntocado(l);
        Movimento(tipo, l.Id, -l.Saldo, l.CustoUnitario, MotivoMovimento.Cancelamento, userId, obs: Limpar(motivo));
        l.Saldo = 0;
        l.CanceladoEm = DateTime.UtcNow;
        l.MotivoCancelamento = Limpar(motivo);
        await db.SaveChangesAsync(ct);
        return l;
    }

    /// <summary>Ajuste manual. Entrada vira um lote novo (pelo custo informado ou pelo custo médio atual, que então não se move);
    /// saída sai proporcional de todos os lotes, para não mexer no custo médio.</summary>
    public async Task AjustarAsync(TipoEstoque tipo, bool entrada, decimal quantidade, decimal? custoUnitario, string motivo, Guid userId, CancellationToken ct)
    {
        if (quantidade <= 0) throw new DomainException("quantidade_invalida", "O valor do ajuste precisa ser maior que zero.");
        var obs = Limpar(motivo) ?? "Ajuste manual";
        if (entrada)
        {
            var custo = custoUnitario is > 0 ? custoUnitario.Value : (await ResumoAsync(tipo, ct)).CustoMedioCalculado;
            if (custo <= 0) throw new DomainException("custo_necessario", "Ainda não há custo médio para basear a entrada. Informe o custo unitário.");
            var lote = new LoteEstoque
            {
                Tipo = tipo, Origem = OrigemLote.Ajuste, DataCompra = FusoSp.Hoje(), Quantidade = quantidade, Saldo = quantidade,
                CustoTotal = Math.Round(quantidade * custo, 8), CustoUnitario = custo, Observacao = obs, CriadoPor = userId,
            };
            db.LotesEstoque.Add(lote);
            Movimento(tipo, lote.Id, quantidade, custo, MotivoMovimento.Ajuste, userId, obs: obs);
        }
        else
        {
            var lotes = await LotesComSaldoAsync(tipo, ct);
            var plano = RegrasEstoque.PlanoProporcional(lotes.Select(l => new SaldoLote(l.Id, l.Saldo, l.CustoUnitario)).ToList(), quantidade, Unidade(tipo));
            Aplicar(tipo, lotes, plano, MotivoMovimento.Ajuste, userId, null, obs);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Define o saldo pelo valor real contado: lança a diferença como ajuste. Devolve a diferença aplicada.</summary>
    public async Task<decimal> DefinirSaldoAsync(TipoEstoque tipo, decimal saldoReal, decimal? custoUnitario, string motivo, Guid userId, CancellationToken ct)
    {
        if (saldoReal < 0) throw new DomainException("saldo_invalido", "Informe um saldo maior ou igual a zero.");
        var atual = (await ResumoAsync(tipo, ct)).Saldo;
        var delta = saldoReal - atual;
        if (delta == 0) throw new DomainException("saldo_igual", "O saldo informado já é o saldo atual.");
        await AjustarAsync(tipo, delta > 0, Math.Abs(delta), custoUnitario, Limpar(motivo) ?? "Definição de saldo", userId, ct);
        return delta;
    }

    /// <summary>Saída por FIFO para uma venda. Falta saldo → 422 e nada muda.</summary>
    public async Task<List<Debito>> DebitarVendaAsync(TipoEstoque tipo, decimal quantidade, Guid vendaId, Guid userId, CancellationToken ct)
    {
        var lotes = await LotesComSaldoAsync(tipo, ct);
        var plano = RegrasEstoque.PlanoFifo(lotes.Select(l => new SaldoLote(l.Id, l.Saldo, l.CustoUnitario)).ToList(), quantidade, Unidade(tipo));
        Aplicar(tipo, lotes, plano, MotivoMovimento.Venda, userId, vendaId, null);
        await db.SaveChangesAsync(ct);
        return plano;
    }

    /// <summary>Devolve aos mesmos lotes tudo o que a venda tirou (do tipo informado, ou de todos), sem mexer no custo médio.</summary>
    public async Task<decimal> EstornarVendaAsync(Guid vendaId, TipoEstoque? tipo, Guid userId, CancellationToken ct)
    {
        var movs = await db.MovimentosEstoque
            .Where(m => m.VendaId == vendaId && m.Motivo == MotivoMovimento.Venda && m.EstornadoEm == null && (tipo == null || m.Tipo == tipo))
            .ToListAsync(ct);
        if (movs.Count == 0) return 0;
        var ids = movs.Select(m => m.LoteId).Distinct().ToList();
        var lotes = await db.LotesEstoque.Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        decimal total = 0;
        foreach (var m in movs)
        {
            var devolver = -m.Quantidade;
            lotes[m.LoteId].Saldo += devolver;
            m.EstornadoEm = DateTime.UtcNow;
            Movimento(m.Tipo, m.LoteId, devolver, m.CustoUnitario, MotivoMovimento.EstornoVenda, userId, vendaId);
            total += devolver;
        }
        await db.SaveChangesAsync(ct);
        return total;
    }

    private void Aplicar(TipoEstoque tipo, List<LoteEstoque> lotes, List<Debito> plano, MotivoMovimento motivo, Guid userId, Guid? vendaId, string? obs)
    {
        var porId = lotes.ToDictionary(l => l.Id);
        foreach (var d in plano)
        {
            porId[d.LoteId].Saldo -= d.Quantidade;
            Movimento(tipo, d.LoteId, -d.Quantidade, d.CustoUnitario, motivo, userId, vendaId, obs);
        }
    }

    /// <summary>Fixa o custo médio do dólar à mão (ou volta ao calculado com null).</summary>
    public async Task<ConfiguracaoEstoque> FixarCustoMedioUsdAsync(decimal? valor, CancellationToken ct)
    {
        if (valor is <= 0) throw new DomainException("custo_invalido", "Informe um custo médio maior que zero, ou deixe vazio para voltar ao cálculo pelos lotes.");
        var c = await ConfigAsync(ct);
        c.CustoMedioUsdFixo = valor;
        c.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return c;
    }

    /// <summary>Traz os lotes com saldo do crivonft (uma vez; reimportar ignora os que já vieram, pelo id de origem).</summary>
    public async Task<ResultadoImportacaoEstoque> ImportarDoCrivonftAsync(Guid userId, CancellationToken ct)
    {
        var usd = await ImportarTipoAsync(TipoEstoque.Usd, "usd", userId, ct);
        var esm = await ImportarTipoAsync(TipoEstoque.Esmeralda, "esmeralda", userId, ct);
        return new ResultadoImportacaoEstoque(usd.Importados, esm.Importados, usd.Ignorados + esm.Ignorados);
    }

    private async Task<(int Importados, int Ignorados)> ImportarTipoAsync(TipoEstoque tipo, string rota, Guid userId, CancellationToken ct)
    {
        var (campoQtd, campoSaldo, campoCusto) = tipo == TipoEstoque.Usd
            ? ("valor_usd_recebido", "saldo_usd", "valor_brl_depositado")
            : ("quantidade_esmeraldas", "saldo_esmeraldas", "custo_usd_compra");
        var existentes = (await db.LotesEstoque.Where(l => l.Tipo == tipo && l.OrigemId != null).Select(l => l.OrigemId!).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int importados = 0, ignorados = 0, offset = 0;
        const int limite = 500;
        while (true)
        {
            var pagina = await crivonft.ExportarEstoqueAsync(rota, limite, offset, ct);
            foreach (var r in pagina.Data)
            {
                var id = Texto(r, "id");
                var saldo = Numero(r, campoSaldo);
                var qtd = Numero(r, campoQtd);
                var data = Data(r, "data_compra");
                if (id is null || saldo is null or <= 0 || qtd is null or <= 0 || data is null || !existentes.Add(id)) { ignorados++; continue; }
                var custoTotal = Numero(r, campoCusto) ?? 0;
                var lote = new LoteEstoque
                {
                    Tipo = tipo, Origem = OrigemLote.ImportacaoCrivonft, OrigemId = id, DataCompra = data.Value, Quantidade = qtd.Value,
                    Saldo = Math.Min(saldo.Value, qtd.Value), CustoTotal = custoTotal, CustoUnitario = RegrasEstoque.CustoUnitario(custoTotal, qtd.Value),
                    Observacao = "Importado do crivonft", CriadoPor = userId,
                };
                db.LotesEstoque.Add(lote);
                Movimento(tipo, lote.Id, lote.Saldo, lote.CustoUnitario, MotivoMovimento.Importacao, userId);
                importados++;
            }
            offset += pagina.Data.Count;
            if (pagina.Data.Count == 0 || offset >= pagina.Total) break;
        }
        await db.SaveChangesAsync(ct);
        return (importados, ignorados);
    }

    // node-pg devolve NUMERIC como texto e DATE como data-hora ISO; aceita os dois formatos.
    private static string? Texto(JsonElement r, string n) =>
        r.TryGetProperty(n, out var v) ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null } : null;

    private static decimal? Numero(JsonElement r, string n) =>
        Texto(r, n) is { } s && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateOnly? Data(JsonElement r, string n)
    {
        var s = Texto(r, n);
        if (s is null) return null;
        if (s.Length == 10 && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)) return null;
        // Meia-noite UTC: o servidor do crivonft roda em UTC e a data é a parte do dia. Outro horário: é meia-noite local, converte para SP.
        return dt.UtcDateTime.TimeOfDay == TimeSpan.Zero ? DateOnly.FromDateTime(dt.UtcDateTime) : FusoSp.Dia(dt.UtcDateTime);
    }

    private static string? Limpar(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > 300 ? t[..300] : t;
    }
}
