using Crivosistemas.Domain.Cambios;
using Crivosistemas.Domain.Common;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Infrastructure.Cambios;

/// <summary>Saldo de câmbios. Toda escrita roda em transação e chama <see cref="TravarAsync"/> antes (mesma ideia do estoque).</summary>
public sealed class CambiosService(AppDbContext db)
{
    private async Task<SaldoCambios> SaldoAsync(CancellationToken ct)
    {
        var s = await db.SaldosCambios.FirstOrDefaultAsync(ct);
        if (s is not null) return s;
        s = new SaldoCambios();
        db.SaldosCambios.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task TravarAsync(CancellationToken ct)
    {
        var s = await SaldoAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM SaldosCambios WHERE Id = {s.Id} FOR UPDATE", ct);
    }

    public async Task<decimal> ObterSaldoAsync(CancellationToken ct) => (await db.SaldosCambios.AsNoTracking().FirstOrDefaultAsync(ct))?.Saldo ?? 0;

    /// <summary>Lança uma variação (positiva entra, negativa sai) e atualiza o saldo. <paramref name="permitirNegativo"/> só para estornos.</summary>
    public async Task<MovimentoCambios> LancarAsync(TipoMovimentoCambios tipo, decimal quantidade, Guid? userId, DateTime? dataHora = null, decimal? valorBrl = null,
        Guid? clienteId = null, Guid? vendaId = null, Guid? estornoDeId = null, string? observacao = null, bool permitirNegativo = false, CancellationToken ct = default)
    {
        var s = await SaldoAsync(ct);
        var novo = s.Saldo + quantidade;
        if (novo < 0 && !permitirNegativo)
            throw new DomainException("saldo_insuficiente", $"Saldo de câmbios insuficiente. Disponível: {s.Saldo:0.##}.");
        var m = new MovimentoCambios
        {
            Tipo = tipo, Quantidade = quantidade, SaldoAnterior = s.Saldo, SaldoNovo = novo, ValorBrl = valorBrl, ClienteId = clienteId, VendaId = vendaId,
            EstornoDeId = estornoDeId, Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim(), UserId = userId, DataHora = dataHora ?? DateTime.UtcNow,
        };
        s.Saldo = novo;
        s.AtualizadoEm = DateTime.UtcNow;
        db.MovimentosCambios.Add(m);
        return m;
    }

    /// <summary>Desfaz um movimento (devolve a variação ao contrário). Só uma vez por movimento.</summary>
    public async Task<MovimentoCambios> EstornarAsync(MovimentoCambios original, Guid? userId, string? motivo, CancellationToken ct)
    {
        if (original.EstornadoEm is not null || original.Tipo == TipoMovimentoCambios.Estorno)
            throw new DomainException("ja_estornado", "Este lançamento já foi estornado ou não pode ser estornado.");
        original.EstornadoEm = DateTime.UtcNow;
        return await LancarAsync(TipoMovimentoCambios.Estorno, -original.Quantidade, userId, valorBrl: original.ValorBrl, clienteId: original.ClienteId,
            vendaId: original.VendaId, estornoDeId: original.Id, observacao: motivo, permitirNegativo: true, ct: ct);
    }

    /// <summary>Venda manual paga em câmbios: entra o saldo. Sem câmbios, não faz nada.</summary>
    public async Task CreditarVendaManualAsync(Guid vendaId, decimal cambios, Guid? clienteId, Guid? userId, CancellationToken ct)
    {
        if (cambios <= 0) return;
        await LancarAsync(TipoMovimentoCambios.VendaManual, cambios, userId, clienteId: clienteId, vendaId: vendaId, ct: ct);
    }

    /// <summary>Cancelamento da venda manual: desfaz o crédito, se houve.</summary>
    public async Task EstornarVendaManualAsync(Guid vendaId, Guid? userId, CancellationToken ct)
    {
        var mov = await db.MovimentosCambios.FirstOrDefaultAsync(m => m.VendaId == vendaId && m.Tipo == TipoMovimentoCambios.VendaManual && m.EstornadoEm == null, ct);
        if (mov is not null) await EstornarAsync(mov, userId, "Venda cancelada", ct);
    }
}
