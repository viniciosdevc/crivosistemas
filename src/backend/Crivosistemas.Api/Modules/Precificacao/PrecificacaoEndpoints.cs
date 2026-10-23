using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Api.Modules.Integracoes;
using Crivosistemas.Domain.Precificacao;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crivosistemas.Api.Modules.Precificacao;

public sealed record ConfigCalculoDto(decimal ValorKFeira, decimal ValorKTrocas, decimal MoedasPorK, decimal DolarPadraoCompra, decimal DolarPadraoVenda,
    decimal TaxaPrata, decimal MargemVendaCambiosPercent);
public sealed record FaixaPixDto(decimal ValorMinUsd, decimal? ValorMaxUsd, decimal Multiplicador, int Ordem, bool Ativo);
public sealed record PrecificacaoDto(int Versao, ConfigCalculoDto Configuracao, IReadOnlyList<FaixaPixDto> FaixasPix, EnvioPrecificacaoDto? UltimoEnvio, bool EnvioConfigurado);
public sealed record SalvarPrecificacaoRequest(
    int Versao,
    [Required(ErrorMessage = "Informe a configuração.")] ConfigCalculoDto Configuracao,
    [Required(ErrorMessage = "Informe as faixas.")] IReadOnlyList<FaixaPixDto> FaixasPix);
public sealed record ImportarRequest(bool Forcar = false);
public sealed record HistoricoPrecificacaoDto(Guid Id, int Versao, string UserNome, string Origem, DateTime CriadoEm, PrecificacaoContrato.PayloadDto Antes, PrecificacaoContrato.PayloadDto Depois);
public sealed record RegrasDto(int Versao, ConfigCalculoDto Configuracao, IReadOnlyList<FaixaPixDto> FaixasPix);
public sealed record CalculoDto(decimal PrecoUsd, decimal PrecoPixBrl, decimal MultiplicadorFaixa, decimal PrecoPelaFaixaBrl, decimal PisoBrl, bool AjustadoPeloPiso, int Versao);

/// <summary>Regras de preço. Este sistema é o dono; salvar enfileira o envio ao crivonft (outbox) e o Worker o entrega.</summary>
public static class PrecificacaoEndpoints
{
    public static void MapPrecificacaoEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/precificacao").WithTags("Precificacao").RequireAuthorization(Session.AdminPolicy);
        g.MapGet("", Obter).WithSummary("Configuração de cálculo, faixas PIX, versão e status do último envio.");
        g.MapPut("", Salvar).WithSummary("Salva o conjunto. Exige a versão carregada (409 se mudou). Sobe a versão, grava histórico e enfileira o envio.");
        g.MapGet("/historico", Historico).WithSummary("Histórico de alterações (quem, quando, antes/depois).");
        g.MapGet("/envios", Envios).WithSummary("Envios ao crivonft (outbox) e seus estados.");
        g.MapPost("/reenviar", Reenviar).WithSummary("Recoloca na fila o último envio que não foi concluído.");
        g.MapPost("/importar-crivonft", Importar).WithSummary("Traz as regras atuais do crivonft (primeira vez, ou com forcar).");
        // Leitura das regras para a calculadora e a venda manual: qualquer pessoa autenticada (o operador também calcula preço).
        app.MapGet("/api/precificacao/regras", Regras).WithTags("Precificacao").RequireAuthorization()
            .WithSummary("Configuração de cálculo e faixas PIX atuais, só leitura.");
        g.MapGet("/calculo", Calcular).WithSummary("Preço PIX final em BRL para um preço em USD, pelas regras salvas.");
    }

    private static ConfigCalculoDto ToDto(PrecificacaoContrato.ConfigDto c) => new(c.ValorKFeira, c.ValorKTrocas, c.MoedasPorK, c.DolarPadraoCompra, c.DolarPadraoVenda, c.TaxaPrata, c.MargemVendaCambiosPercent);
    private static PrecificacaoContrato.ConfigDto ToContrato(ConfigCalculoDto c) => new(c.ValorKFeira, c.ValorKTrocas, c.MoedasPorK, c.DolarPadraoCompra, c.DolarPadraoVenda, c.TaxaPrata, c.MargemVendaCambiosPercent);

    private static async Task<PrecificacaoDto> Montar(PrecificacaoContrato.PayloadDto p, AppDbContext db, IOptions<CrivonftOptions> o, CancellationToken ct)
    {
        var envio = await db.PrecificacaoEnvios.AsNoTracking().OrderByDescending(e => e.CriadoEm).FirstOrDefaultAsync(ct);
        return new PrecificacaoDto(p.Versao, ToDto(p.ConfiguracaoCalculo),
            p.FaixasPix.Select(f => new FaixaPixDto(f.ValorMinUsd, f.ValorMaxUsd, f.Multiplicador, f.Ordem, f.Ativo)).ToList(),
            envio is null ? null : IntegracoesEndpoints.ToDto(envio), o.Value.Envia);
    }

    private static async Task<IResult> Regras(PrecificacaoService svc, CancellationToken ct)
    {
        var p = await svc.ObterAsync(ct);
        return Results.Ok(new RegrasDto(p.Versao, ToDto(p.ConfiguracaoCalculo),
            p.FaixasPix.Select(f => new FaixaPixDto(f.ValorMinUsd, f.ValorMaxUsd, f.Multiplicador, f.Ordem, f.Ativo)).ToList()));
    }

    private static async Task<IResult> Obter(PrecificacaoService svc, AppDbContext db, IOptions<CrivonftOptions> o, CancellationToken ct) =>
        Results.Ok(await Montar(await svc.ObterAsync(ct), db, o, ct));

    private static async Task<IResult> Salvar([FromBody] SalvarPrecificacaoRequest req, PrecificacaoService svc, AppDbContext db, IOptions<CrivonftOptions> o, HttpContext http, CancellationToken ct)
    {
        var nome = http.User.Identity?.Name ?? "?";
        var salvo = await svc.SalvarAsync(ToContrato(req.Configuracao),
            req.FaixasPix.Select(f => new PrecificacaoContrato.FaixaDto(f.ValorMinUsd, f.ValorMaxUsd, f.Multiplicador, f.Ordem, f.Ativo)).ToList(),
            req.Versao, http.User.Id(), nome, ct);
        return Results.Ok(await Montar(salvo, db, o, ct));
    }

    private static async Task<IResult> Importar([FromBody] ImportarRequest? req, PrecificacaoService svc, AppDbContext db, IOptions<CrivonftOptions> o, HttpContext http, CancellationToken ct)
    {
        if (!o.Value.Envia) return Results.Json(new { title = "A integração com o crivonft não está configurada.", code = "integracao_nao_configurada", status = 503 }, statusCode: 503);
        try
        {
            var p = await svc.ImportarDoCrivonftAsync(req?.Forcar == true, http.User.Id(), http.User.Identity?.Name ?? "?", ct);
            return Results.Ok(await Montar(p, db, o, ct));
        }
        catch (CrivonftException e)
        {
            return Results.Json(new { title = e.Message, code = "crivonft_indisponivel", status = 502 }, statusCode: 502);
        }
    }

    private static async Task<IResult> Historico(AppDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var r = await db.PrecificacaoHistoricos.AsNoTracking().OrderByDescending(h => h.Versao).ThenByDescending(h => h.CriadoEm).ToPageAsync(page, pageSize, ct);
        return Results.Ok(new PagedResult<HistoricoPrecificacaoDto>(r.Items.Select(h => new HistoricoPrecificacaoDto(h.Id, h.Versao, h.UserNome, h.Origem, h.CriadoEm,
            PrecificacaoContrato.Ler(h.Antes)!, PrecificacaoContrato.Ler(h.Depois)!)).ToList(), r.Page, r.PageSize, r.Total));
    }

    private static async Task<IResult> Envios(AppDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var r = await db.PrecificacaoEnvios.AsNoTracking().OrderByDescending(e => e.CriadoEm).ThenBy(e => e.Id).ToPageAsync(page, pageSize, ct);
        return Results.Ok(new PagedResult<EnvioPrecificacaoDto>(r.Items.Select(IntegracoesEndpoints.ToDto).ToList(), r.Page, r.PageSize, r.Total));
    }

    private static async Task<IResult> Reenviar(PrecificacaoEnvioService envio, CancellationToken ct) =>
        await envio.ReenviarUltimoAsync(ct) ? Results.Accepted() : Results.Json(new { title = "Não há envio pendente ou com falha.", code = "nada_a_reenviar", status = 409 }, statusCode: 409);

    private static async Task<IResult> Calcular(PrecificacaoService svc, decimal? usd, CancellationToken ct)
    {
        if (usd is null or < 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["usd"] = ["Informe um preço em USD maior ou igual a zero."] });
        var p = await svc.ObterAsync(ct);
        var (cfg, faixas) = PrecificacaoService.ParaCalculo(p);
        var r = CalculoPreco.PrecoPix((double)usd, cfg, faixas);
        return Results.Ok(new CalculoDto(usd.Value, (decimal)r.PrecoPixBrl, (decimal)r.FaixaMultiplicador, (decimal)r.PrecoPelaFaixa, (decimal)r.PisoBrl, r.AjustadoPeloPiso, p.Versao));
    }
}
