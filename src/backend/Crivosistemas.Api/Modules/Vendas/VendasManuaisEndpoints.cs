using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Domain.Common;
using Crivosistemas.Infrastructure.Cambios;
using Crivosistemas.Domain.Estoque;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Precificacao;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Estoque;
using Crivosistemas.Infrastructure.Financeiro;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crivosistemas.Api.Modules.Vendas;

public sealed record NovaVendaRequest(
    DateTime? DataHora,
    [StringLength(120, ErrorMessage = "Nickname muito longo.")] string? NicknameComprador,
    [Required(ErrorMessage = "Informe o nome do item."), StringLength(300, MinimumLength = 1, ErrorMessage = "Informe o nome do item.")] string NomeItem,
    [Required(ErrorMessage = "Informe a forma de pagamento.")] string FormaPagamento,
    [Required(ErrorMessage = "Informe de onde saiu o item.")] string OrigemCusto,
    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Custo USD inválido.")] decimal CustoUsd,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Custo em câmbios inválido.")] decimal CustoCambios,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Quantidade de esmeraldas inválida.")] decimal QuantidadeEsmeraldas,
    bool UsaTaxaPrata,
    [Range(typeof(decimal), "0", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Valor PIX inválido.")] decimal ValorVendaPix,
    [Range(typeof(decimal), "0", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "Valor em câmbios inválido.")] decimal ValorVendaCambios,
    [StringLength(500, ErrorMessage = "Observação muito longa.")] string? Observacao,
    bool Entregue = false);

public sealed record CancelarVendaRequest([StringLength(300, ErrorMessage = "Motivo muito longo.")] string? Motivo);

/// <summary>Vendas lançadas à mão aqui (origem "manual"). O servidor recalcula custo, lucro e margem com as regras de preço salvas
/// e mexe no estoque: esmeraldas saem na criação, o dólar sai (FIFO) quando a venda é marcada como entregue. Cancelar devolve aos mesmos lotes.</summary>
public static class VendasManuaisEndpoints
{
    public static void MapVendasManuaisEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/vendas").WithTags("Vendas").RequireAuthorization();
        g.MapPost("/manuais", Criar).WithSummary("Registra uma venda manual (pendente, ou já entregue).");
        g.MapPut("/{id:guid}/manual", Editar).WithSummary("Edita uma venda manual (não cancelada): refaz o cálculo e acerta estoque, câmbios e financeiro.");
        g.MapPost("/{id:guid}/entregar", Entregar).WithSummary("Marca a venda manual como entregue: entra no faturamento e debita o dólar.");
        g.MapPost("/{id:guid}/cancelar", Cancelar).WithSummary("Cancela a venda manual e devolve o estoque.");
    }

    private const string StatusPendente = "pendente", StatusEntregue = "entregue", StatusCancelada = "cancelada";

    private static async Task<IResult> Criar([FromBody] NovaVendaRequest req, AppDbContext db, PrecificacaoService precos, EstoqueService estoque, CambiosService cambiosSvc,
        IngestaoService ingestao, FinanceiroService financeiro, HttpContext http, CancellationToken ct)
    {
        if (Validar(req, out var forma, out var origemCusto, out var pix, out var cambios) is { } erro) return erro;

        var userId = http.User.Id();
        var nomeUsuario = http.User.Identity?.Name;
        var venda = await db.InTransactionAsync(async () =>
        {
            await estoque.TravarAsync(ct);
            await cambiosSvc.TravarAsync(ct);
            var calc = await CalcularAsync(req, origemCusto, pix, cambios, precos, estoque, ct);

            Cliente? cliente = null;
            var nick = req.NicknameComprador?.Trim();
            if (!string.IsNullOrEmpty(nick)) cliente = await ingestao.AplicarClienteAsync(new ClientePayload(nick, nick, null, null, null), sobrescrever: false, ct);

            var agora = DateTime.UtcNow;
            var v = new Venda
            {
                Origem = OrigemVenda.Manual,
                OrigemId = "",
                DataHora = (req.DataHora ?? agora).ToUniversalTime(),
                Status = StatusPendente,
                StatusNormalizado = StatusVenda.Pendente,
                FormaPagamento = forma,
                Vendedor = nomeUsuario,
                Cliente = cliente,
                ClienteId = cliente?.Id,
                TotalBrl = (decimal)calc.ReceitaTotalBrl,
                ValorPixBrl = pix,
                ValorCambios = cambios,
                CustoBrl = (decimal)calc.CustoBrl,
                CustoUsd = (decimal)calc.CustoUsd,
                CustoCambios = (decimal)calc.CustoCambios,
                LucroBrl = (decimal)calc.LucroBrl,
                MargemPercent = (decimal)calc.MargemPercent,
                OrigemCusto = origemCusto,
                QuantidadeEsmeraldas = origemCusto == OrigemCusto.EstoqueEsmeralda ? req.QuantidadeEsmeraldas : null,
                UsaTaxaPrata = req.UsaTaxaPrata,
                DolarCompraUsado = calc.DolarCompraUsado is { } d ? (decimal)d : null,
                Observacao = string.IsNullOrWhiteSpace(req.Observacao) ? null : req.Observacao.Trim(),
                CriadoPor = userId,
                AtualizadoEmOrigem = agora,
            };
            v.OrigemId = v.Id.ToString();
            v.Itens.Add(new VendaItem { VendaId = v.Id, Ordem = 0, Descricao = req.NomeItem.Trim(), Quantidade = 1, PrecoUsd = (decimal)calc.CustoUsd, PrecoBrl = v.TotalBrl });
            db.Vendas.Add(v);
            await db.SaveChangesAsync(ct);

            // Esmeraldas saem já na criação (reserva física, como no crivonft).
            if (origemCusto == OrigemCusto.EstoqueEsmeralda)
                await estoque.DebitarVendaAsync(TipoEstoque.Esmeralda, req.QuantidadeEsmeraldas, v.Id, userId, ct);
            if (req.Entregue) await MarcarEntregueAsync(v, estoque, cambiosSvc, financeiro, userId, ct);

            await VendaEnvioService.EnfileirarAsync(db, v.Id, ct);
            db.Audit(http, "venda.manual_criada", "Venda", v.Id, new { v.TotalBrl, v.LucroBrl, origemCusto = Nomes.Custo(origemCusto), req.Entregue });
            await db.SaveChangesAsync(ct);
            return v;
        }, ct);
        return Results.Created($"/api/vendas/{venda.Id}", VendasEndpoints.ToLista(venda));
    }

    /// <summary>Valida o corpo de nova venda/edição; devolve o erro HTTP (ou null) e os valores já normalizados.</summary>
    private static IResult? Validar(NovaVendaRequest req, out FormaPagamento forma, out OrigemCusto origemCusto, out decimal pix, out decimal cambios)
    {
        var erros = new Dictionary<string, string[]>();
        pix = cambios = 0;
        if (!Nomes.TentarForma(req.FormaPagamento, out forma)) erros["formaPagamento"] = ["Use pix, cambios ou hibrido."];
        if (!Nomes.TentarCusto(req.OrigemCusto, out origemCusto)) erros["origemCusto"] = ["Use estoque_usd, cambios, estoque_esmeralda ou manual."];
        if (string.IsNullOrWhiteSpace(req.NomeItem)) erros["nomeItem"] = ["Informe o nome do item."];
        if (erros.Count > 0) return Results.ValidationProblem(erros);

        pix = forma == FormaPagamento.Cambios ? 0 : req.ValorVendaPix;
        cambios = forma == FormaPagamento.Pix ? 0 : Math.Round(req.ValorVendaCambios, 0, MidpointRounding.AwayFromZero);
        if (pix + cambios <= 0) throw new DomainException("valor_obrigatorio", "Informe quanto o cliente pagou.");
        if (origemCusto == OrigemCusto.EstoqueEsmeralda && req.QuantidadeEsmeraldas <= 0)
            throw new DomainException("esmeraldas_obrigatorias", "Informe quantas esmeraldas saíram do estoque.");
        if (origemCusto == OrigemCusto.Cambios && req.CustoCambios <= 0)
            throw new DomainException("custo_obrigatorio", "Informe o custo em câmbios.");
        return null;
    }

    /// <summary>Custo efetivo (mesma regra do crivonft): dólar do estoque pelo custo médio; esmeraldas pelo custo médio delas em US$. Chamar com o estoque travado.</summary>
    private static async Task<VendaCalculada> CalcularAsync(NovaVendaRequest req, OrigemCusto origemCusto, decimal pix, decimal cambios, PrecificacaoService precos, EstoqueService estoque, CancellationToken ct)
    {
            var (cfg, faixas) = PrecificacaoService.ParaCalculo(await precos.ObterAsync(ct));
            if (!req.UsaTaxaPrata) cfg = cfg with { TaxaPrata = 0 };

            // Custo efetivo (mesma regra do crivonft): dólar do estoque pelo custo médio; esmeraldas pelo custo médio delas em US$.
            var custoUsd = (double)req.CustoUsd;
            double? dolarCompra = null;
            if (origemCusto == OrigemCusto.EstoqueUsd)
            {
                var r = await estoque.ResumoAsync(TipoEstoque.Usd, ct);
                if (r.CustoMedio > 0) dolarCompra = (double)r.CustoMedio;
            }
            if (origemCusto == OrigemCusto.EstoqueEsmeralda)
            {
                var r = await estoque.ResumoAsync(TipoEstoque.Esmeralda, ct);
                if (req.QuantidadeEsmeraldas > r.Saldo)
                    throw new DomainException("saldo_insuficiente", $"Saldo de esmeraldas insuficiente. Disponível: {r.Saldo:0.##}.");
                if (r.CustoMedio <= 0) throw new DomainException("custo_medio_ausente", "As esmeraldas em estoque estão sem custo. Confira o estoque.");
                custoUsd = (double)(req.QuantidadeEsmeraldas * r.CustoMedio);
            }
            var calc = CalculoVenda.Venda(origemCusto == OrigemCusto.Cambios ? 0 : custoUsd, (double)pix, (double)cambios, cfg, faixas,
                origemCusto == OrigemCusto.Cambios ? (double)req.CustoCambios : null, dolarCompra);
        return calc;
    }

    /// <summary>Editar = desfazer os efeitos da venda (estoque, saldo de câmbios, financeiro) e aplicá-los de novo com os valores novos,
    /// na mesma transação. Assim estoque e financeiro sempre refletem o estado final, sem lógica de diferença.</summary>
    private static async Task<IResult> Editar(Guid id, [FromBody] NovaVendaRequest req, AppDbContext db, PrecificacaoService precos, EstoqueService estoque, CambiosService cambiosSvc,
        IngestaoService ingestao, FinanceiroService financeiro, HttpContext http, CancellationToken ct)
    {
        if (Validar(req, out var forma, out var origemCusto, out var pix, out var cambios) is { } erro) return erro;
        var userId = http.User.Id();
        var venda = await db.InTransactionAsync(async () =>
        {
            await estoque.TravarAsync(ct);
            await cambiosSvc.TravarAsync(ct);
            var v = await ManualAsync(db, id, ct);
            if (v.StatusNormalizado == StatusVenda.Cancelado) throw new DomainException("status_invalido", "Venda cancelada não pode ser editada.");
            var antes = new { v.TotalBrl, v.LucroBrl, v.Status, item = v.Itens.OrderBy(i => i.Ordem).FirstOrDefault()?.Descricao };

            await estoque.EstornarVendaAsync(v.Id, null, userId, ct);
            await cambiosSvc.EstornarVendaManualAsync(v.Id, userId, ct);
            var calc = await CalcularAsync(req, origemCusto, pix, cambios, precos, estoque, ct);

            var nick = req.NicknameComprador?.Trim();
            Cliente? cliente = string.IsNullOrEmpty(nick) ? null : await ingestao.AplicarClienteAsync(new ClientePayload(nick, nick, null, null, null), sobrescrever: false, ct);
            v.Cliente = cliente;
            v.ClienteId = cliente?.Id;
            if (req.DataHora is { } dh) v.DataHora = dh.ToUniversalTime();
            v.FormaPagamento = forma;
            v.TotalBrl = (decimal)calc.ReceitaTotalBrl;
            v.ValorPixBrl = pix;
            v.ValorCambios = cambios;
            v.CustoBrl = (decimal)calc.CustoBrl;
            v.CustoUsd = (decimal)calc.CustoUsd;
            v.CustoCambios = (decimal)calc.CustoCambios;
            v.LucroBrl = (decimal)calc.LucroBrl;
            v.MargemPercent = (decimal)calc.MargemPercent;
            v.OrigemCusto = origemCusto;
            v.QuantidadeEsmeraldas = origemCusto == OrigemCusto.EstoqueEsmeralda ? req.QuantidadeEsmeraldas : null;
            v.UsaTaxaPrata = req.UsaTaxaPrata;
            v.DolarCompraUsado = calc.DolarCompraUsado is { } d ? (decimal)d : null;
            v.Observacao = string.IsNullOrWhiteSpace(req.Observacao) ? null : req.Observacao.Trim();
            v.Status = StatusPendente;
            v.StatusNormalizado = StatusVenda.Pendente;
            v.AtualizadoEm = v.AtualizadoEmOrigem = DateTime.UtcNow;
            var item = v.Itens.OrderBy(i => i.Ordem).FirstOrDefault();
            if (item is null) v.Itens.Add(new VendaItem { VendaId = v.Id, Ordem = 0, Descricao = req.NomeItem.Trim(), Quantidade = 1, PrecoUsd = (decimal)calc.CustoUsd, PrecoBrl = v.TotalBrl });
            else { item.Descricao = req.NomeItem.Trim(); item.PrecoUsd = (decimal)calc.CustoUsd; item.PrecoBrl = v.TotalBrl; }
            await db.SaveChangesAsync(ct);

            if (origemCusto == OrigemCusto.EstoqueEsmeralda)
                await estoque.DebitarVendaAsync(TipoEstoque.Esmeralda, req.QuantidadeEsmeraldas, v.Id, userId, ct);
            if (req.Entregue) await MarcarEntregueAsync(v, estoque, cambiosSvc, financeiro, userId, ct);
            else await financeiro.SincronizarVendaAsync(v, ct);

            await VendaEnvioService.EnfileirarAsync(db, v.Id, ct);
            db.Audit(http, "venda.manual_editada", "Venda", v.Id, new { antes, depois = new { v.TotalBrl, v.LucroBrl, v.Status, item = req.NomeItem.Trim() }, origemCusto = Nomes.Custo(origemCusto) });
            await db.SaveChangesAsync(ct);
            return v;
        }, ct);
        return Results.Ok(VendasEndpoints.ToLista(venda));
    }

    private static async Task MarcarEntregueAsync(Venda v, EstoqueService estoque, CambiosService cambios, FinanceiroService financeiro, Guid userId, CancellationToken ct)
    {
        // Venda paga em câmbios gera saldo (só as lançadas aqui; as importadas do crivonft não entram).
        await cambios.CreditarVendaManualAsync(v.Id, v.ValorCambios, v.ClienteId, userId, ct);
        if (v.OrigemCusto == OrigemCusto.EstoqueUsd && v.CustoUsd > 0)
            await estoque.DebitarVendaAsync(TipoEstoque.Usd, v.CustoUsd, v.Id, userId, ct);
        v.Status = StatusEntregue;
        v.StatusNormalizado = StatusVenda.Concluido;
        v.AtualizadoEm = v.AtualizadoEmOrigem = DateTime.UtcNow;
        // Entregue: o PIX entra no financeiro (como "Venda PIX" no crivonft).
        await financeiro.SincronizarVendaAsync(v, ct);
    }

    private static async Task<Venda> ManualAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var v = await db.Vendas.Include(x => x.Cliente).Include(x => x.Itens).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Venda não encontrada.");
        if (!v.Manual) throw new DomainException("venda_nao_manual", "Só vendas lançadas aqui podem ser alteradas. As do crivonft mudam por lá.");
        return v;
    }

    private static async Task<IResult> Entregar(Guid id, AppDbContext db, EstoqueService estoque, CambiosService cambios, FinanceiroService financeiro, HttpContext http, CancellationToken ct)
    {
        var v = await db.InTransactionAsync(async () =>
        {
            await estoque.TravarAsync(ct);
            await cambios.TravarAsync(ct);
            var v = await ManualAsync(db, id, ct);
            if (v.StatusNormalizado != StatusVenda.Pendente) throw new DomainException("status_invalido", "Só uma venda pendente pode ser marcada como entregue.");
            await MarcarEntregueAsync(v, estoque, cambios, financeiro, http.User.Id(), ct);
            await VendaEnvioService.EnfileirarAsync(db, v.Id, ct);
            db.Audit(http, "venda.manual_entregue", "Venda", v.Id);
            await db.SaveChangesAsync(ct);
            return v;
        }, ct);
        return Results.Ok(VendasEndpoints.ToLista(v));
    }

    private static async Task<IResult> Cancelar(Guid id, [FromBody] CancelarVendaRequest? req, AppDbContext db, EstoqueService estoque, CambiosService cambios, FinanceiroService financeiro, HttpContext http, CancellationToken ct)
    {
        var v = await db.InTransactionAsync(async () =>
        {
            await estoque.TravarAsync(ct);
            await cambios.TravarAsync(ct);
            var v = await ManualAsync(db, id, ct);
            if (v.StatusNormalizado == StatusVenda.Cancelado) throw new DomainException("status_invalido", "Esta venda já está cancelada.");
            var devolvido = await estoque.EstornarVendaAsync(v.Id, null, http.User.Id(), ct);
            await cambios.EstornarVendaManualAsync(v.Id, http.User.Id(), ct);
            v.Status = StatusCancelada;
            v.StatusNormalizado = StatusVenda.Cancelado;
            v.AtualizadoEm = v.AtualizadoEmOrigem = DateTime.UtcNow;
            await financeiro.SincronizarVendaAsync(v, ct);
            if (!string.IsNullOrWhiteSpace(req?.Motivo))
            {
                var obs = $"{v.Observacao}\nCancelada: {req.Motivo.Trim()}".Trim();
                v.Observacao = obs.Length > 500 ? obs[..500] : obs;
            }
            await VendaEnvioService.EnfileirarAsync(db, v.Id, ct);
            db.Audit(http, "venda.manual_cancelada", "Venda", v.Id, new { motivo = req?.Motivo, devolvido });
            await db.SaveChangesAsync(ct);
            return v;
        }, ct);
        return Results.Ok(VendasEndpoints.ToLista(v));
    }
}
