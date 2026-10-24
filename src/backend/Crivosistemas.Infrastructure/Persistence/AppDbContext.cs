using Crivosistemas.Domain.Audit;
using Crivosistemas.Domain.Common;
using Crivosistemas.Domain.Estoque;
using Crivosistemas.Domain.Financeiro;
using Crivosistemas.Domain.Identity;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Precificacao;
using Crivosistemas.Domain.Vendas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Crivosistemas.Infrastructure.Persistence;

/// <summary>Contexto único do sistema. É single-tenant (uma empresa): não há filtro por loja nem trava entre contas.</summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Venda> Vendas => Set<Venda>();
    public DbSet<VendaItem> VendaItens => Set<VendaItem>();
    public DbSet<EventoRecebido> EventosRecebidos => Set<EventoRecebido>();
    public DbSet<ReconciliacaoExecucao> Reconciliacoes => Set<ReconciliacaoExecucao>();
    public DbSet<PrecificacaoConfig> PrecificacaoConfigs => Set<PrecificacaoConfig>();
    public DbSet<FaixaPix> FaixasPix => Set<FaixaPix>();
    public DbSet<PrecificacaoHistorico> PrecificacaoHistoricos => Set<PrecificacaoHistorico>();
    public DbSet<PrecificacaoEnvio> PrecificacaoEnvios => Set<PrecificacaoEnvio>();
    public DbSet<LoteEstoque> LotesEstoque => Set<LoteEstoque>();
    public DbSet<MovimentoEstoque> MovimentosEstoque => Set<MovimentoEstoque>();
    public DbSet<ConfiguracaoEstoque> ConfiguracoesEstoque => Set<ConfiguracaoEstoque>();
    public DbSet<LancamentoFinanceiro> LancamentosFinanceiros => Set<LancamentoFinanceiro>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // O provedor MySQL lê TIME/DATE como TimeSpan/DateTime; converte explicitamente.
        configurationBuilder.Properties<TimeOnly>().HaveConversion<TimeOnlyConverter>().HaveColumnType("time");
        // Datas ficam em UTC. O MySQL devolve Kind=Unspecified, que o JSON serializaria sem "Z" e o navegador leria como hora local.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateOnly>().HaveConversion<DateOnlyConverter>().HaveColumnType("date");
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class TimeOnlyConverter() : ValueConverter<TimeOnly, TimeSpan>(t => t.ToTimeSpan(), s => TimeOnly.FromTimeSpan(s));

    private sealed class DateOnlyConverter() : ValueConverter<DateOnly, DateTime>(d => d.ToDateTime(TimeOnly.MinValue), d => DateOnly.FromDateTime(d));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Ids são gerados no código (Guid v7). Marcar como "nunca gerado pelo banco" faz o EF tratar filhos novos
            // ligados por navegação como inserts, e não como updates de linhas inexistentes.
            entityType.FindProperty(nameof(Entity.Id))?.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
    }
}
