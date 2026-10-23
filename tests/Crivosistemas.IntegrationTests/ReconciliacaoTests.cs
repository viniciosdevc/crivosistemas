using System.Net;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Vendas;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Crivosistemas.IntegrationTests;

[Collection("api")]
public sealed class ReconciliacaoTests(ApiFactory factory)
{
    /// <summary>O banco é compartilhado entre os testes: encerra pedidos pendentes de outros testes para que a fila só tenha o do teste atual.</summary>
    private async Task LimparFila()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Reconciliacoes.Where(r => r.Estado == EstadoReconciliacao.Pendente || r.Estado == EstadoReconciliacao.Executando)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.Estado, EstadoReconciliacao.Concluida));
    }

    private static string Linha(string id, string nick, string status, decimal receita, string dh, string atualizado, string? pedido = null) =>
        $$"""
        {"id":"{{id}}","pedido_id":{{(pedido is null ? "null" : $"\"{pedido}\"")}},"data_hora":"{{dh}}","atualizado_em":"{{atualizado}}","status":"{{status}}",
         "nickname_comprador":"{{nick}}","nome_item":"Item {{id}}","receita_total_brl":{{receita}},"custo_brl":10,"lucro_brl":{{receita - 10}},"margem_percent":5,
         "valor_venda_pix":{{receita}},"valor_venda_cambios":0,"custo_usd":0,"custo_cambios":0,"forma_pagamento":"pix","vendedor":"ana"}
        """;

    [Fact]
    public async Task Pedido_manual_fica_pendente_o_worker_puxa_vendas_e_clientes_e_reaplicar_nao_duplica()
    {
        await LimparFila();
        var tag = "rec" + Guid.NewGuid().ToString("N")[..8];
        var f = factory.Crivonft;
        f.Vendas.Clear(); f.Clientes.Clear(); f.ConsultasVendas.Clear();
        f.Clientes.Add(FakeCrivonftClient.Linha($$"""{"nickname":"{{tag}}-x","primeira_compra":"2025-01-01T00:00:00Z"}"""));
        f.Vendas.Add(FakeCrivonftClient.Linha(Linha($"{tag}-1", $"{tag}-x", "entregue", 100m, "2025-05-01T15:00:00Z", "2025-05-01T16:00:00Z")));
        f.Vendas.Add(FakeCrivonftClient.Linha(Linha($"{tag}-2", $"{tag}-x", "cancelado", 40m, "2025-05-02T15:00:00Z", "2025-05-02T16:00:00Z")));
        f.Vendas.Add(FakeCrivonftClient.Linha(Linha($"{tag}-3", $"{tag}-x", "entregue", 77m, "2025-05-03T15:00:00Z", "2025-05-03T16:00:00Z", pedido: "ped-1"))); // espelho do site: ignorada

        var admin = await factory.SeedAndLoginAdminAsync();
        var pedido = await (await admin.Post("/api/integracoes/crivonft/reconciliacoes", new { de = "2025-05-01", ate = "2025-05-03" })).Json();
        Assert.Equal("pendente", pedido.GetProperty("estado").GetString());
        Assert.Equal("manual", pedido.GetProperty("tipo").GetString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ReconciliacaoService>();
            var exec = await svc.ReivindicarPendenteAsync(CancellationToken.None);
            Assert.NotNull(exec);
            await svc.ExecutarAsync(exec!, CancellationToken.None);
            Assert.Equal(EstadoReconciliacao.Concluida, exec!.Estado);
            Assert.Equal(3, exec.VendasLidas);
            Assert.Equal(2, exec.VendasAplicadas);
            Assert.Equal(1, exec.ClientesCriados);
        }
        Assert.Equal((new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 3)), (f.ConsultasVendas[0].De!.Value, f.ConsultasVendas[0].Ate!.Value));

        var vendas = await (await admin.GetAsync($"/api/vendas?busca={tag}&incluirRemovidas=true")).Json();
        Assert.Equal(2, vendas.GetProperty("total").GetInt32());
        var concl = vendas.GetProperty("items").EnumerateArray().First(v => v.GetProperty("statusNormalizado").GetString() == "concluido");
        Assert.Equal(100m, concl.GetProperty("totalBrl").GetDecimal());
        Assert.Equal("nft", concl.GetProperty("origem").GetString());

        // As vendas exportadas ficam ligadas ao cliente pelo nickname (Clientes mostra total gasto e compras)
        var cli = await (await admin.GetAsync($"/api/clientes?busca={tag}")).Json();
        Assert.Equal(1, cli.GetProperty("total").GetInt32());
        Assert.Equal(1, cli.GetProperty("items")[0].GetProperty("compras").GetInt32());
        Assert.Equal(100m, cli.GetProperty("items")[0].GetProperty("totalGasto").GetDecimal());

        // Roda de novo: mesma regra de upsert, nada duplicado
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ReconciliacaoService>();
            await svc.SolicitarAsync(new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 3), null, CancellationToken.None);
            var exec = (await svc.ReivindicarPendenteAsync(CancellationToken.None))!;
            await svc.ExecutarAsync(exec, CancellationToken.None);
            Assert.Equal(0, exec.ClientesCriados);
        }
        Assert.Equal(2, (await (await admin.GetAsync($"/api/vendas?busca={tag}&incluirRemovidas=true")).Json()).GetProperty("total").GetInt32());

        var lista = await (await admin.GetAsync("/api/integracoes/crivonft/reconciliacoes")).Json();
        Assert.Equal("concluida", lista.GetProperty("items")[0].GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Carga_inicial_pagina_ate_o_fim_e_falha_de_rede_marca_a_execucao_como_falhou()
    {
        var f = factory.Crivonft;
        f.Vendas.Clear(); f.Clientes.Clear(); f.ConsultasVendas.Clear();
        var tag = "ini" + Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 2300; i++)
            f.Vendas.Add(FakeCrivonftClient.Linha(Linha($"{tag}-{i}", $"{tag}-c{i % 5}", "entregue", 10m, "2024-02-01T12:00:00Z", "2024-02-01T12:00:00Z")));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var e = await scope.ServiceProvider.GetRequiredService<ReconciliacaoService>().CargaInicialAsync(new DateOnly(2024, 1, 1), CancellationToken.None);
            Assert.Equal(EstadoReconciliacao.Concluida, e.Estado);
            Assert.Equal(2300, e.VendasLidas);
            Assert.Equal(2300, e.VendasAplicadas);
            Assert.Equal(new[] { 0, 1000, 2000 }, f.ConsultasVendas.Select(c => c.Offset).ToArray());
            Assert.All(f.ConsultasVendas, c => Assert.Equal(new DateOnly(2024, 1, 1), c.De));
        }
        var admin = await factory.SeedAndLoginAdminAsync();
        Assert.Equal(2300, (await (await admin.GetAsync($"/api/vendas?busca={tag}&pageSize=1")).Json()).GetProperty("total").GetInt32());

        f.ExportConfigurado = false;
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var e = await scope.ServiceProvider.GetRequiredService<ReconciliacaoService>().CargaInicialAsync(null, CancellationToken.None);
            Assert.Equal(EstadoReconciliacao.Falhou, e.Estado);
            Assert.False(string.IsNullOrEmpty(e.Erro));
        }
        finally { f.ExportConfigurado = true; f.Vendas.Clear(); }
    }

    [Fact]
    public async Task Rodada_noturna_e_agendada_uma_vez_por_dia_depois_das_3h_de_sao_paulo_e_cobre_3_dias()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ReconciliacaoService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Reconciliacoes.Where(r => r.Tipo == TipoReconciliacao.Noturna).ExecuteDeleteAsync();

        var antesDas3 = new DateTime(2031, 6, 15, 5, 0, 0, DateTimeKind.Utc); // 02:00 em SP
        Assert.Null(await svc.AgendarNoturnaSeNecessarioAsync(antesDas3, CancellationToken.None));

        var agora = DateTime.UtcNow;
        var local = agora.AddHours(-3);
        if (local.Hour < 3) return; // antes das 3h em SP a regra não agenda; coberto acima
        var e = await svc.AgendarNoturnaSeNecessarioAsync(agora, CancellationToken.None);
        Assert.NotNull(e);
        Assert.Equal(DateOnly.FromDateTime(local), e!.Ate);
        Assert.Equal(DateOnly.FromDateTime(local).AddDays(-2), e.De);
        Assert.Null(await svc.AgendarNoturnaSeNecessarioAsync(agora, CancellationToken.None));
    }

    [Fact]
    public async Task Pedido_de_reconciliacao_valida_o_periodo_e_responde_503_sem_configuracao()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/integracoes/crivonft/reconciliacoes", new { de = "2025-02-02", ate = "2025-01-01" })).StatusCode);

        using var semConfig = factory.WithWebHostBuilder(b => b.UseSetting("Integrations:Crivonft:ExportToken", "").UseSetting("Integrations:Crivonft:Token", ""));
        await factory.SeedAndLoginAdminAsync("rec-sem-config@teste.dev");
        var c = await semConfig.LoginComo("rec-sem-config@teste.dev");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.Post("/api/integracoes/crivonft/reconciliacoes", new { })).StatusCode);
    }

    [Fact]
    public async Task Venda_gravada_sem_cliente_ganha_o_cliente_na_reconciliacao_mesmo_com_atualizado_em_igual()
    {
        await LimparFila();
        var tag = "lig" + Guid.NewGuid().ToString("N")[..8];
        var f = factory.Crivonft;
        f.Vendas.Clear(); f.Clientes.Clear();
        var t = new DateTime(2025, 6, 1, 15, 0, 0, DateTimeKind.Utc);
        // Já gravada sem cliente (como ficou em produção)
        await factory.Maquina().Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda($"{tag}-1", t, 80m, nick: null)));
        f.Vendas.Add(FakeCrivonftClient.Linha(Linha($"{tag}-1", $"{tag}-Fulano", "entregue", 80m, t.ToString("O"), t.ToString("O"))));

        var admin = await factory.SeedAndLoginAdminAsync();
        Assert.Equal(0, (await (await admin.GetAsync($"/api/clientes?busca={tag}")).Json()).GetProperty("total").GetInt32());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ReconciliacaoService>();
            await svc.SolicitarAsync(new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 1), null, CancellationToken.None);
            await svc.ExecutarAsync((await svc.ReivindicarPendenteAsync(CancellationToken.None))!, CancellationToken.None);
        }
        var cli = (await (await admin.GetAsync($"/api/clientes?busca={tag}")).Json()).GetProperty("items")[0];
        Assert.Equal(1, cli.GetProperty("compras").GetInt32());
        Assert.Equal(80m, cli.GetProperty("totalGasto").GetDecimal());
    }

    [Fact]
    public async Task Lista_de_clientes_padrao_por_ultima_compra_traz_quem_comprou_antes_dos_alfabeticamente_primeiros()
    {
        var f = factory.Crivonft;
        var tag = "ord" + Guid.NewGuid().ToString("N")[..8];
        f.Vendas.Clear(); f.Clientes.Clear();
        // Muitos clientes só do histórico completo (sem vendas importadas) e um comprador recente com nome alfabeticamente último
        for (var i = 0; i < 30; i++) f.Clientes.Add(FakeCrivonftClient.Linha($$"""{"nickname":"{{tag}}-a{{i:00}}"}"""));
        f.Clientes.Add(FakeCrivonftClient.Linha($$"""{"nickname":"{{tag}}-zz"}"""));
        var t = DateTime.UtcNow.AddDays(-1);
        f.Vendas.Add(FakeCrivonftClient.Linha(Linha($"{tag}-1", $"{tag}-zz", "entregue", 55m, t.ToString("O"), t.ToString("O"))));
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ReconciliacaoService>().CargaInicialAsync(FusoSp.Hoje().AddDays(-3), CancellationToken.None);

        var admin = await factory.SeedAndLoginAdminAsync();
        var p1 = await (await admin.GetAsync($"/api/clientes?busca={tag}&ordem=ultimaCompra&dir=desc&pageSize=5")).Json();
        Assert.Equal($"{tag}-zz", p1.GetProperty("items")[0].GetProperty("nickname").GetString());
        Assert.Equal(55m, p1.GetProperty("items")[0].GetProperty("totalGasto").GetDecimal());
    }

    [Fact]
    public async Task Reconciliacao_pelo_reenviar_pede_janelas_de_um_mes_divide_quando_trunca_e_nao_usa_o_export()
    {
        await LimparFila();
        var f = factory.Crivonft;
        f.Vendas.Clear(); f.Clientes.Clear(); f.Reenvios.Clear(); f.ConsultasVendas.Clear();
        f.RotaReenviarExiste = true;
        f.TruncarAcimaDeDias = 20;
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<ReconciliacaoService>();
            await svc.SolicitarAsync(new DateOnly(2025, 1, 15), new DateOnly(2025, 3, 10), null, CancellationToken.None);
            var e = (await svc.ReivindicarPendenteAsync(CancellationToken.None))!;
            await svc.ExecutarAsync(e, CancellationToken.None);

            Assert.Equal(EstadoReconciliacao.Concluida, e.Estado);
            Assert.Null(e.Aviso);
            Assert.Empty(f.ConsultasVendas); // nada do export
            // Janelas: 15-31/01 (17 dias), 01-28/02 (28 dias, trunca e divide em duas), 01-10/03 (10 dias)
            Assert.Equal(new DateOnly(2025, 1, 15), f.Reenvios[0].De);
            Assert.Equal(new DateOnly(2025, 1, 31), f.Reenvios[0].Ate);
            Assert.Equal((new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28)), f.Reenvios[1]);
            Assert.Equal((new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 14)), f.Reenvios[2]);
            Assert.Equal((new DateOnly(2025, 2, 15), new DateOnly(2025, 2, 28)), f.Reenvios[3]);
            Assert.Equal((new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 10)), f.Reenvios[4]);
            Assert.Equal(5, f.Reenvios.Count);
            Assert.True(e.VendasLidas > 0);
        }
        finally { f.RotaReenviarExiste = false; f.TruncarAcimaDeDias = int.MaxValue; }
    }

    [Fact]
    public async Task Sem_a_rota_reenviar_cai_no_export_e_registra_aviso_de_execucao_parcial()
    {
        await LimparFila();
        var f = factory.Crivonft;
        f.Vendas.Clear(); f.Clientes.Clear();
        f.RotaReenviarExiste = false;
        await using var scope = factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ReconciliacaoService>();
        await svc.SolicitarAsync(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2), null, CancellationToken.None);
        var e = (await svc.ReivindicarPendenteAsync(CancellationToken.None))!;
        await svc.ExecutarAsync(e, CancellationToken.None);
        Assert.Equal(EstadoReconciliacao.Concluida, e.Estado);
        Assert.Equal("crivonft desatualizado: só vendas NFT", e.Aviso);

        var admin = await factory.SeedAndLoginAdminAsync();
        var lista = await (await admin.GetAsync("/api/integracoes/crivonft/reconciliacoes")).Json();
        Assert.Equal("crivonft desatualizado: só vendas NFT", lista.GetProperty("items")[0].GetProperty("aviso").GetString());
    }

    [Fact]
    public async Task Erro_de_autenticacao_no_reenviar_falha_com_mensagem_clara()
    {
        await LimparFila();
        var f = factory.Crivonft;
        f.RotaReenviarExiste = true; f.StatusDoReenvio = 401;
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<ReconciliacaoService>();
            await svc.SolicitarAsync(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2), null, CancellationToken.None);
            var e = (await svc.ReivindicarPendenteAsync(CancellationToken.None))!;
            await svc.ExecutarAsync(e, CancellationToken.None);
            Assert.Equal(EstadoReconciliacao.Falhou, e.Estado);
            Assert.Equal("Token ou segredo diferentes nos dois sistemas.", e.Erro);
        }
        finally { f.RotaReenviarExiste = false; f.StatusDoReenvio = 200; }
    }

    [Fact]
    public async Task Importar_historico_cria_uma_carga_inicial_pendente_so_para_admin()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        var op = await factory.CreateAndLoginAsync(admin, "operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await op.Post("/api/integracoes/crivonft/importar-historico", new { desde = "2025-01-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post("/api/integracoes/crivonft/importar-historico", new { desde = "2999-01-01" })).StatusCode);
        var r = await (await admin.Post("/api/integracoes/crivonft/importar-historico", new { desde = "2025-01-01" })).Json();
        Assert.Equal("carga_inicial", r.GetProperty("tipo").GetString());
        Assert.Equal("pendente", r.GetProperty("estado").GetString());
        Assert.Equal("2025-01-01", r.GetProperty("de").GetString());
    }
}
