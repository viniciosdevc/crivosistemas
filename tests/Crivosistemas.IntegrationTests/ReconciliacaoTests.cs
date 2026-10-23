using System.Net;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Crivosistemas.IntegrationTests;

[Collection("api")]
public sealed class ReconciliacaoTests(ApiFactory factory)
{
    private static string Linha(string id, string nick, string status, decimal receita, string dh, string atualizado, string? pedido = null) =>
        $$"""
        {"id":"{{id}}","pedido_id":{{(pedido is null ? "null" : $"\"{pedido}\"")}},"data_hora":"{{dh}}","atualizado_em":"{{atualizado}}","status":"{{status}}",
         "nickname_comprador":"{{nick}}","nome_item":"Item {{id}}","receita_total_brl":{{receita}},"custo_brl":10,"lucro_brl":{{receita - 10}},"margem_percent":5,
         "valor_venda_pix":{{receita}},"valor_venda_cambios":0,"custo_usd":0,"custo_cambios":0,"forma_pagamento":"pix","vendedor":"ana"}
        """;

    [Fact]
    public async Task Pedido_manual_fica_pendente_o_worker_puxa_vendas_e_clientes_e_reaplicar_nao_duplica()
    {
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

        using var semConfig = factory.WithWebHostBuilder(b => b.UseSetting("Integrations:Crivonft:ExportToken", ""));
        await factory.SeedAndLoginAdminAsync("rec-sem-config@teste.dev");
        var c = await semConfig.LoginComo("rec-sem-config@teste.dev");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.Post("/api/integracoes/crivonft/reconciliacoes", new { })).StatusCode);
    }
}
