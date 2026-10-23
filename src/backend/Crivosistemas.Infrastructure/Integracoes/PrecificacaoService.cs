using Crivosistemas.Domain.Audit;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Precificacao;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crivosistemas.Infrastructure.Integracoes;

/// <summary>Regras de precificação. Este sistema é o dono: cada salvamento sobe a versão, grava histórico (quem, quando, antes/depois),
/// auditoria e uma linha no outbox, tudo na mesma transação. O Worker envia o outbox ao crivonft.</summary>
public sealed class PrecificacaoService(AppDbContext db, ICrivonftClient crivonft, ILogger<PrecificacaoService> logger)
{
    /// <summary>Estado atual. Sem registro ainda, devolve os padrões de negócio com versão 0.</summary>
    public async Task<PrecificacaoContrato.PayloadDto> ObterAsync(CancellationToken ct)
    {
        var cfg = await db.PrecificacaoConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var faixas = await db.FaixasPix.AsNoTracking().OrderBy(f => f.Ordem).ThenBy(f => f.ValorMinUsd).ToListAsync(ct);
        return Montar(cfg, faixas);
    }

    /// <summary>Salva o conjunto completo. <paramref name="versaoEsperada"/> é a versão que o painel carregou: se mudou, 409.</summary>
    public async Task<PrecificacaoContrato.PayloadDto> SalvarAsync(PrecificacaoContrato.ConfigDto config, IReadOnlyList<PrecificacaoContrato.FaixaDto> faixas,
        int versaoEsperada, Guid? userId, string userNome, CancellationToken ct) =>
        await GravarAsync(config, faixas, versaoEsperada, userId, userNome, "painel", enfileirar: true, versaoMinima: 0, ct);

    /// <summary>Traz o estado atual do crivonft (GET da mesma rota). Só na primeira vez (versão local 0), a menos que <paramref name="forcar"/>.
    /// Não enfileira envio: o crivonft já tem esses valores.</summary>
    public async Task<PrecificacaoContrato.PayloadDto> ImportarDoCrivonftAsync(bool forcar, Guid? userId, string userNome, CancellationToken ct)
    {
        var atual = await ObterAsync(ct);
        if (atual.Versao > 0 && !forcar) throw new ConflictException("precificacao_ja_inicializada", "As regras já foram definidas aqui; use forcar para sobrescrever pelas do crivonft.");
        if (!crivonft.EnvioConfigurado) throw new CrivonftException("Integração com o crivonft não configurada (BaseUrl, Token e HmacSecret).");
        var remoto = await crivonft.LerPrecificacaoAsync(ct);
        // A versão local nunca regride: o crivonft só aceita versão maior que a que ele tem.
        return await GravarAsync(remoto.ConfiguracaoCalculo, remoto.FaixasPix, atual.Versao, userId, userNome, "importacao_crivonft",
            enfileirar: false, versaoMinima: remoto.Versao, ct);
    }

    private async Task<PrecificacaoContrato.PayloadDto> GravarAsync(PrecificacaoContrato.ConfigDto config, IReadOnlyList<PrecificacaoContrato.FaixaDto> faixas,
        int versaoEsperada, Guid? userId, string userNome, string origem, bool enfileirar, int versaoMinima, CancellationToken ct)
    {
        var erros = PrecificacaoContrato.Validar(config, faixas);
        if (erros.Count > 0) throw new DomainException("precificacao_invalida", string.Join(" ", erros));

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var cfg = await db.PrecificacaoConfigs.FirstOrDefaultAsync(ct);
            var antes = Montar(cfg, await db.FaixasPix.AsNoTracking().OrderBy(f => f.Ordem).ThenBy(f => f.ValorMinUsd).ToListAsync(ct));
            if (antes.Versao != versaoEsperada)
                throw new ConflictException("precificacao_versao", $"As regras foram alteradas por outra pessoa (versão atual {antes.Versao}). Recarregue e tente de novo.");

            if (cfg is null) { cfg = new PrecificacaoConfig(); db.PrecificacaoConfigs.Add(cfg); }
            cfg.ValorKFeira = config.ValorKFeira; cfg.ValorKTrocas = config.ValorKTrocas; cfg.MoedasPorK = config.MoedasPorK;
            cfg.DolarPadraoCompra = config.DolarPadraoCompra; cfg.DolarPadraoVenda = config.DolarPadraoVenda; cfg.TaxaPrata = config.TaxaPrata;
            cfg.MargemVendaCambiosPercent = config.MargemVendaCambiosPercent;
            cfg.Versao = Math.Max(antes.Versao + 1, versaoMinima);
            cfg.AtualizadoEm = DateTime.UtcNow;
            cfg.AtualizadoPor = userId;

            db.FaixasPix.RemoveRange(await db.FaixasPix.ToListAsync(ct));
            var ordem = 0;
            foreach (var f in faixas.OrderBy(f => f.ValorMinUsd).ThenBy(f => f.Ordem))
                db.FaixasPix.Add(new FaixaPix { ValorMinUsd = f.ValorMinUsd, ValorMaxUsd = f.ValorMaxUsd, Multiplicador = f.Multiplicador, Ordem = ordem++, Ativo = f.Ativo });

            var depois = Montar(cfg, db.FaixasPix.Local.OrderBy(f => f.Ordem).ToList());
            db.PrecificacaoHistoricos.Add(new PrecificacaoHistorico
            {
                Versao = cfg.Versao, UserId = userId, UserNome = userNome, Origem = origem,
                Antes = PrecificacaoContrato.Serializar(antes), Depois = PrecificacaoContrato.Serializar(depois),
            });
            db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "precificacao.salva", EntityType = "Precificacao", Data = $"{{\"versao\":{cfg.Versao},\"origem\":\"{origem}\"}}" });

            if (enfileirar)
            {
                // Só a versão mais nova importa: as pendentes anteriores são descartadas.
                var antigas = await db.PrecificacaoEnvios.Where(x => x.Estado == EstadoEnvio.Pendente || x.Estado == EstadoEnvio.Falhou).ToListAsync(ct);
                foreach (var a in antigas) a.Estado = EstadoEnvio.Substituido;
                db.PrecificacaoEnvios.Add(new PrecificacaoEnvio { Versao = cfg.Versao, Payload = PrecificacaoContrato.Serializar(depois) });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            logger.LogInformation("Precificação salva na versão {Versao} ({Origem})", cfg.Versao, origem);
            return depois;
        });
    }

    private static PrecificacaoContrato.PayloadDto Montar(PrecificacaoConfig? cfg, IReadOnlyList<FaixaPix> faixas)
    {
        if (cfg is null)
        {
            var p = ConfigCalculo.Padrao;
            return new(0, new((decimal)p.ValorKFeira, (decimal)p.ValorKTrocas, (decimal)p.MoedasPorK, (decimal)p.DolarPadraoCompra, (decimal)p.DolarPadraoVenda,
                    (decimal)p.TaxaPrata, (decimal)p.MargemVendaCambiosPercent),
                CalculoPreco.FaixasPadrao.Select((f, i) => new PrecificacaoContrato.FaixaDto((decimal)f.ValorMinUsd, (decimal?)f.ValorMaxUsd, (decimal)f.Multiplicador, i, true)).ToList());
        }
        return new(cfg.Versao,
            new(cfg.ValorKFeira, cfg.ValorKTrocas, cfg.MoedasPorK, cfg.DolarPadraoCompra, cfg.DolarPadraoVenda, cfg.TaxaPrata, cfg.MargemVendaCambiosPercent),
            faixas.Select(f => new PrecificacaoContrato.FaixaDto(f.ValorMinUsd, f.ValorMaxUsd, f.Multiplicador, f.Ordem, f.Ativo)).ToList());
    }

    /// <summary>Converte o estado salvo nos tipos do cálculo.</summary>
    public static (ConfigCalculo Config, List<FaixaCalculo> Faixas) ParaCalculo(PrecificacaoContrato.PayloadDto p) =>
        (new((double)p.ConfiguracaoCalculo.ValorKFeira, (double)p.ConfiguracaoCalculo.ValorKTrocas, (double)p.ConfiguracaoCalculo.MoedasPorK,
            (double)p.ConfiguracaoCalculo.DolarPadraoCompra, (double)p.ConfiguracaoCalculo.DolarPadraoVenda, (double)p.ConfiguracaoCalculo.TaxaPrata,
            (double)p.ConfiguracaoCalculo.MargemVendaCambiosPercent),
         p.FaixasPix.Select(f => new FaixaCalculo((double)f.ValorMinUsd, (double?)f.ValorMaxUsd, (double)f.Multiplicador, f.Ordem, f.Ativo)).ToList());
}

/// <summary>Envia ao crivonft as regras pendentes do outbox, com backoff. Roda no Worker.</summary>
public sealed class PrecificacaoEnvioService(AppDbContext db, ICrivonftClient crivonft, ILogger<PrecificacaoEnvioService> logger)
{
    /// <summary>Tenta enviar o que está vencido. Devolve quantos foram enviados com sucesso.</summary>
    public async Task<int> ProcessarAsync(CancellationToken ct)
    {
        if (!crivonft.EnvioConfigurado) return 0;
        var agora = DateTime.UtcNow;
        var envio = await db.PrecificacaoEnvios
            .Where(x => x.Estado == EstadoEnvio.Pendente && x.ProximaTentativaEm <= agora)
            .OrderByDescending(x => x.Versao).FirstOrDefaultAsync(ct);
        if (envio is null) return 0;

        envio.Tentativas++;
        envio.UltimaTentativaEm = agora;
        try
        {
            var r = await crivonft.EnviarPrecificacaoAsync(envio.Payload, ct);
            envio.UltimoStatusHttp = r.StatusHttp;
            if (r.Sucesso)
            {
                envio.Estado = EstadoEnvio.Enviado;
                envio.EnviadoEm = DateTime.UtcNow;
                envio.UltimoErro = null;
                // Qualquer pendente mais antiga perdeu o sentido.
                foreach (var o in await db.PrecificacaoEnvios.Where(x => x.Estado == EstadoEnvio.Pendente && x.Versao < envio.Versao).ToListAsync(ct))
                    o.Estado = EstadoEnvio.Substituido;
            }
            else
            {
                Falha(envio, $"HTTP {r.StatusHttp}: {Cortar(r.Corpo, 500)}", EnvioRegras.ErroPermanente(r.StatusHttp));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            envio.UltimoStatusHttp = (ex as CrivonftException)?.StatusHttp;
            Falha(envio, ex.Message, permanente: false);
        }
        await db.SaveChangesAsync(CancellationToken.None);
        if (envio.Estado == EstadoEnvio.Falhou) logger.LogWarning("Envio da precificação v{Versao} desistiu: {Erro}", envio.Versao, envio.UltimoErro);
        return envio.Estado == EstadoEnvio.Enviado ? 1 : 0;
    }

    private static void Falha(PrecificacaoEnvio e, string erro, bool permanente)
    {
        e.UltimoErro = Cortar(erro, 1900);
        if (permanente || e.Tentativas >= EnvioRegras.MaxTentativas) { e.Estado = EstadoEnvio.Falhou; return; }
        e.ProximaTentativaEm = DateTime.UtcNow + EnvioRegras.Atraso(e.Tentativas);
    }

    /// <summary>Recoloca na fila o último envio que falhou, para tentar de novo agora.</summary>
    public async Task<bool> ReenviarUltimoAsync(CancellationToken ct)
    {
        var ultimo = await db.PrecificacaoEnvios.OrderByDescending(x => x.Versao).ThenByDescending(x => x.CriadoEm).FirstOrDefaultAsync(ct);
        if (ultimo is null || ultimo.Estado is EstadoEnvio.Enviado) return false;
        ultimo.Estado = EstadoEnvio.Pendente;
        ultimo.Tentativas = 0;
        ultimo.ProximaTentativaEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string Cortar(string? s, int max) => s is null ? "" : s.Length <= max ? s : s[..max];
}
