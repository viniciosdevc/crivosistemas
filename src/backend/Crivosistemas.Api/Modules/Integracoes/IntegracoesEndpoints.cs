using System.ComponentModel.DataAnnotations;
using Crivosistemas.Api.Common;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Precificacao;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crivosistemas.Api.Modules.Integracoes;

public sealed record EventoDto(Guid Id, Guid EventoId, string Tipo, string Estado, DateTime OcorridoEm, DateTime RecebidoEm, DateTime? ProcessadoEm,
    int Tentativas, string? Resultado, string? Erro, string? Origem, string? OrigemId, string? ClienteChave);
public sealed record EventoDetalheDto(EventoDto Evento, string Corpo);
public sealed record ReconciliacaoDto(Guid Id, string Tipo, string Estado, DateOnly? De, DateOnly? Ate, DateTime CriadoEm, DateTime? IniciadoEm, DateTime? ConcluidoEm,
    int VendasLidas, int VendasAplicadas, int ClientesLidos, int ClientesCriados, string? Erro, string? Aviso);
public sealed record ReconciliarRequest(DateOnly? De, DateOnly? Ate);
public sealed record ImportarHistoricoRequest(DateOnly? Desde);
public sealed record EnvioPrecificacaoDto(Guid Id, int Versao, string Estado, int Tentativas, DateTime CriadoEm, DateTime? UltimaTentativaEm, DateTime? ProximaTentativaEm,
    DateTime? EnviadoEm, int? UltimoStatusHttp, string? UltimoErro);
public sealed record StatusIntegracaoDto(bool RecebeEventos, bool Envia, bool Exporta, DateTime? UltimoEventoRecebidoEm, Dictionary<string, int> EventosPorEstado,
    IReadOnlyList<EventoDto> UltimosErros, ReconciliacaoDto? UltimaReconciliacao, int VersaoPrecificacao, EnvioPrecificacaoDto? UltimoEnvioPrecificacao);

/// <summary>Integração com o crivonft: recepção de eventos (máquina a máquina) e telas administrativas da página Integrações.</summary>
public static class IntegracoesEndpoints
{
    public const string RotaEventos = "/api/integracoes/crivonft/eventos";
    private const int MaxCorpo = 2 * 1024 * 1024;

    public static void MapIntegracoesEndpoints(this IEndpointRouteBuilder app)
    {
        // Sem cookie nem CSRF: a autenticação é Bearer + HMAC. Ver docs/integracao-crivonft.md.
        app.MapPost(RotaEventos, Receber).AllowAnonymous().WithTags("Integracoes")
            .WithSummary("Recebe um evento do crivonft (Bearer + HMAC, sem sessão).");

        var admin = app.MapGroup("/api/integracoes/crivonft").WithTags("Integracoes").RequireAuthorization(Session.AdminPolicy);
        admin.MapGet("/status", Status).WithSummary("Painel da tela Integrações: último evento, contagens por estado, últimos erros.");
        admin.MapGet("/eventos", Listar).WithSummary("Eventos recebidos, com filtros (estado, tipo, busca por id/origem/cliente).");
        admin.MapGet("/eventos/{id:guid}", Detalhe).WithSummary("Um evento com o corpo bruto.");
        admin.MapPost("/eventos/{id:guid}/reprocessar", Reprocessar).WithSummary("Reaplica um evento gravado.");
        admin.MapGet("/reconciliacoes", ListarReconciliacoes).WithSummary("Últimas execuções de reconciliação.");
        admin.MapPost("/reconciliacoes", Reconciliar).WithSummary("Pede uma reconciliação de um período (o Worker executa).");
        admin.MapGet("/vendas-manuais", StatusVendasManuais).WithSummary("Envio das vendas manuais ao crivonft: quantas por estado e os últimos erros.");
        admin.MapPost("/vendas-manuais/reenviar", ReenviarVendasManuais).WithSummary("Recoloca todas as vendas manuais na fila de envio ao crivonft (carga inicial ou depois de uma falha).");
        admin.MapPost("/importar-historico", ImportarHistorico).WithSummary("Carga inicial: pede ao crivonft o reenvio de todo o histórico desde uma data (o Worker executa, em janelas de 1 mês).");
    }

    // ── vendas manuais → crivonft ───────────────────────────────────────────

    private static async Task<IResult> StatusVendasManuais(AppDbContext db, CancellationToken ct)
    {
        var porEstado = await db.VendaEnvios.GroupBy(x => x.Estado).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var erros = await db.VendaEnvios.Where(x => x.UltimoErro != null && x.Estado != EstadoEnvio.Enviado).OrderByDescending(x => x.UltimaTentativaEm).Take(10)
            .Select(x => new { x.VendaId, estado = x.Estado.ToString().ToLower(), x.Tentativas, x.UltimoStatusHttp, x.UltimoErro }).ToListAsync(ct);
        return Results.Ok(new { porEstado = porEstado.ToDictionary(x => x.Key.ToString().ToLower(), x => x.N), ultimosErros = erros });
    }

    private static async Task<IResult> ReenviarVendasManuais(HttpContext http, AppDbContext db, VendaEnvioService envio, CancellationToken ct)
    {
        var n = await envio.EnfileirarTodasAsync(ct);
        db.Audit(http, "integracoes.vendas_manuais_reenviadas", "Venda", null, new { vendas = n });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { enfileiradas = n });
    }

    // ── recepção ────────────────────────────────────────────────────────────

    private static async Task<IResult> Receber(HttpContext http, IngestaoService ingestao, IOptions<CrivonftOptions> opcoes, ILogger<IngestaoService> logger, CancellationToken ct)
    {
        var o = opcoes.Value;
        if (!o.RecebeEventos) return Problema(StatusCodes.Status503ServiceUnavailable, "A integração com o crivonft não está configurada.", "integracao_nao_configurada");

        var corpo = await LerCorpoAsync(http.Request, ct);
        if (corpo is null) return Problema(StatusCodes.Status400BadRequest, "Corpo grande demais.", "corpo_grande");

        var auth = CrivoAuth.Verificar(o.Token, o.HmacSecret, http.Request.Headers.Authorization.ToString(),
            http.Request.Headers[CrivoAuth.CabecalhoTimestamp].ToString(), http.Request.Headers[CrivoAuth.CabecalhoAssinatura].ToString(),
            corpo, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (auth != ResultadoAuth.Ok)
        {
            logger.LogWarning("Evento do crivonft recusado: {Motivo}", auth);
            return Problema(StatusCodes.Status401Unauthorized, auth == ResultadoAuth.TimestampInvalido ? "Timestamp fora da janela de 5 minutos." : "Credenciais inválidas.",
                auth == ResultadoAuth.TimestampInvalido ? "timestamp_invalido" : "nao_autorizado");
        }

        var r = await ingestao.ReceberAsync(System.Text.Encoding.UTF8.GetString(corpo), ct);
        return r.Desfecho switch
        {
            DesfechoIngestao.Processado => Results.Ok(new { ok = true, duplicado = false, resultado = r.Resultado }),
            DesfechoIngestao.Duplicado => Results.Ok(new { duplicado = true }),
            DesfechoIngestao.Rejeitado => Results.Json(new { title = "Payload inválido.", code = "payload_invalido", erros = r.Erros }, statusCode: StatusCodes.Status400BadRequest),
            _ => Problema(StatusCodes.Status500InternalServerError, "Falha ao processar o evento; repita.", "falha_processamento"),
        };
    }

    private static async Task<byte[]?> LerCorpoAsync(HttpRequest req, CancellationToken ct)
    {
        if (req.ContentLength > MaxCorpo) return null;
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await req.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > MaxCorpo) return null;
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    private static IResult Problema(int status, string titulo, string codigo) => Results.Json(new { title = titulo, code = codigo, status }, statusCode: status);

    // ── telas administrativas ───────────────────────────────────────────────

    internal static EventoDto ToDto(EventoRecebido e) => new(e.Id, e.EventoId, e.Tipo, Nome(e.Estado), e.OcorridoEm, e.RecebidoEm, e.ProcessadoEm,
        e.Tentativas, e.Resultado, e.Erro, e.Origem, e.OrigemId, e.ClienteChave);

    internal static string Nome(EstadoEvento e) => e switch
    {
        EstadoEvento.Recebido => "recebido", EstadoEvento.Processado => "processado", EstadoEvento.Falhou => "falhou", _ => "rejeitado",
    };

    internal static ReconciliacaoDto ToDto(ReconciliacaoExecucao r) => new(r.Id, r.Tipo switch { TipoReconciliacao.Noturna => "noturna", TipoReconciliacao.Manual => "manual", _ => "carga_inicial" },
        r.Estado switch { EstadoReconciliacao.Pendente => "pendente", EstadoReconciliacao.Executando => "executando", EstadoReconciliacao.Concluida => "concluida", _ => "falhou" },
        r.De, r.Ate, r.CriadoEm, r.IniciadoEm, r.ConcluidoEm, r.VendasLidas, r.VendasAplicadas, r.ClientesLidos, r.ClientesCriados, r.Erro, r.Aviso);

    internal static EnvioPrecificacaoDto ToDto(PrecificacaoEnvio e) => new(e.Id, e.Versao, e.Estado switch
        {
            EstadoEnvio.Pendente => "pendente", EstadoEnvio.Enviado => "enviado", EstadoEnvio.Falhou => "falhou", _ => "substituido",
        }, e.Tentativas, e.CriadoEm, e.UltimaTentativaEm, e.Estado == EstadoEnvio.Pendente ? e.ProximaTentativaEm : null, e.EnviadoEm, e.UltimoStatusHttp, e.UltimoErro);

    private static async Task<IResult> Status(AppDbContext db, IOptions<CrivonftOptions> opcoes, CancellationToken ct)
    {
        var o = opcoes.Value;
        var contagens = await db.EventosRecebidos.GroupBy(e => e.Estado).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var porEstado = Enum.GetValues<EstadoEvento>().ToDictionary(Nome, e => contagens.FirstOrDefault(c => c.Key == e)?.N ?? 0);
        var ultimo = await db.EventosRecebidos.MaxAsync(e => (DateTime?)e.RecebidoEm, ct);
        var erros = await db.EventosRecebidos.AsNoTracking().Where(e => e.Estado == EstadoEvento.Falhou || e.Estado == EstadoEvento.Rejeitado)
            .OrderByDescending(e => e.RecebidoEm).Take(10).ToListAsync(ct);
        var rec = await db.Reconciliacoes.AsNoTracking().OrderByDescending(r => r.CriadoEm).FirstOrDefaultAsync(ct);
        var versao = await db.PrecificacaoConfigs.Select(c => c.Versao).FirstOrDefaultAsync(ct);
        var envio = await db.PrecificacaoEnvios.AsNoTracking().OrderByDescending(e => e.CriadoEm).FirstOrDefaultAsync(ct);
        return Results.Ok(new StatusIntegracaoDto(o.RecebeEventos, o.Envia, o.Exporta, ultimo, porEstado, erros.Select(ToDto).ToList(),
            rec is null ? null : ToDto(rec), versao, envio is null ? null : ToDto(envio)));
    }

    private static async Task<IResult> Listar(AppDbContext db, string? estado, string? tipo, string? busca, int? page, int? pageSize, CancellationToken ct)
    {
        var q = db.EventosRecebidos.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(estado))
        {
            var valido = Enum.GetValues<EstadoEvento>().Where(e => Nome(e) == estado.Trim().ToLowerInvariant()).ToList();
            if (valido.Count == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["estado"] = ["Use recebido, processado, falhou ou rejeitado."] });
            var s = valido[0];
            q = q.Where(e => e.Estado == s);
        }
        if (!string.IsNullOrWhiteSpace(tipo)) { var t = tipo.Trim(); q = q.Where(e => e.Tipo == t); }
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim();
            if (Guid.TryParse(b, out var g)) q = q.Where(e => e.EventoId == g || e.Id == g);
            else q = q.Where(e => e.OrigemId!.Contains(b) || e.ClienteChave!.Contains(b) || e.Erro!.Contains(b));
        }
        var r = await q.OrderByDescending(e => e.RecebidoEm).ThenBy(e => e.Id).ToPageAsync(page, pageSize, ct);
        return Results.Ok(new PagedResult<EventoDto>(r.Items.Select(ToDto).ToList(), r.Page, r.PageSize, r.Total));
    }

    private static async Task<IResult> Detalhe(Guid id, AppDbContext db, CancellationToken ct)
    {
        var e = await db.EventosRecebidos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Evento não encontrado.");
        return Results.Ok(new EventoDetalheDto(ToDto(e), e.Corpo));
    }

    private static async Task<IResult> Reprocessar(Guid id, AppDbContext db, IngestaoService ingestao, HttpContext http, CancellationToken ct)
    {
        var r = await ingestao.ReprocessarAsync(id, ct) ?? throw new NotFoundException("Evento não encontrado.");
        db.Audit(http, "integracoes.evento_reprocessado", "EventoRecebido", id, new { desfecho = r.Desfecho.ToString() });
        await db.SaveChangesAsync(ct);
        var e = await db.EventosRecebidos.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return Results.Ok(ToDto(e));
    }

    private static async Task<IResult> ListarReconciliacoes(AppDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var r = await db.Reconciliacoes.AsNoTracking().OrderByDescending(x => x.CriadoEm).ThenBy(x => x.Id).ToPageAsync(page, pageSize, ct);
        return Results.Ok(new PagedResult<ReconciliacaoDto>(r.Items.Select(ToDto).ToList(), r.Page, r.PageSize, r.Total));
    }

    private static async Task<IResult> Reconciliar([FromBody] ReconciliarRequest req, AppDbContext db, ReconciliacaoService servico, IOptions<CrivonftOptions> opcoes,
        HttpContext http, CancellationToken ct)
    {
        if (!opcoes.Value.Envia && !opcoes.Value.Exporta) return Problema(StatusCodes.Status503ServiceUnavailable, "A integração com o crivonft não está configurada (BaseUrl, Token, HmacSecret e ExportToken).", "integracao_nao_configurada");
        var hoje = Crivosistemas.Domain.Vendas.FusoSp.Hoje();
        var ate = req.Ate ?? hoje;
        var de = req.De ?? ate.AddDays(-(ReconciliacaoService.DiasNoturna - 1));
        if (de > ate) return Results.ValidationProblem(new Dictionary<string, string[]> { ["de"] = ["A data inicial deve ser anterior ou igual à final."] });
        if (ate.DayNumber - de.DayNumber > 366) return Results.ValidationProblem(new Dictionary<string, string[]> { ["ate"] = ["O período máximo é de 366 dias."] });
        var e = await servico.SolicitarAsync(de, ate, http.User.Id(), ct);
        db.Audit(http, "integracoes.reconciliacao_solicitada", "ReconciliacaoExecucao", e.Id, new { de, ate });
        await db.SaveChangesAsync(ct);
        return Results.Accepted($"/api/integracoes/crivonft/reconciliacoes", ToDto(e));
    }

    private static async Task<IResult> ImportarHistorico([FromBody] ImportarHistoricoRequest? req, AppDbContext db, IOptions<CrivonftOptions> opcoes, HttpContext http, CancellationToken ct)
    {
        if (!opcoes.Value.Envia && !opcoes.Value.Exporta) return Problema(StatusCodes.Status503ServiceUnavailable, "A integração com o crivonft não está configurada.", "integracao_nao_configurada");
        var desde = req?.Desde ?? ReconciliacaoService.DesdePadrao;
        var hoje = Crivosistemas.Domain.Vendas.FusoSp.Hoje();
        if (desde > hoje) return Results.ValidationProblem(new Dictionary<string, string[]> { ["desde"] = ["A data não pode ser futura."] });
        var e = new ReconciliacaoExecucao { Tipo = TipoReconciliacao.CargaInicial, De = desde, Ate = hoje, SolicitadoPor = http.User.Id() };
        db.Reconciliacoes.Add(e);
        db.Audit(http, "integracoes.importar_historico", "ReconciliacaoExecucao", e.Id, new { desde });
        await db.SaveChangesAsync(ct);
        return Results.Accepted("/api/integracoes/crivonft/reconciliacoes", ToDto(e));
    }

    /// <summary>Falha ao chamar o crivonft: 502 com um detalhe claro (o 503 fica reservado ao nginx "atualizando").</summary>
    internal static IResult Crivonft502(CrivonftException e) => Results.Json(new
    {
        title = CrivonftClient.Explicar(e), detail = CrivonftClient.Explicar(e), code = "crivonft_indisponivel", status = 502, statusCrivonft = e.StatusHttp,
    }, statusCode: StatusCodes.Status502BadGateway);
}
