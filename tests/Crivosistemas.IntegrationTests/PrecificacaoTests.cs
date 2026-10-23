using System.Net;
using System.Text.Json;
using Crivosistemas.Domain.Precificacao;
using Crivosistemas.Infrastructure.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Crivosistemas.IntegrationTests;

/// <summary>Precificação é um singleton no banco compartilhado: este arquivo roda os cenários em um único teste sequencial.</summary>
[Collection("api")]
public sealed class PrecificacaoTests(ApiFactory factory)
{
    private static object Corpo(int versao, decimal dolarVenda = 6m, decimal mult = 6.5m) => new
    {
        versao,
        configuracao = new { valorKFeira = 13, valorKTrocas = 12, moedasPorK = 1000, dolarPadraoCompra = 5.7m, dolarPadraoVenda = dolarVenda, taxaPrata = 3, margemVendaCambiosPercent = 20 },
        faixasPix = new object[]
        {
            new { valorMinUsd = 0, valorMaxUsd = (decimal?)10, multiplicador = mult, ordem = 0, ativo = true },
            new { valorMinUsd = 10.01m, valorMaxUsd = (decimal?)null, multiplicador = 6m, ordem = 1, ativo = true },
        },
    };

    [Fact]
    public async Task Fluxo_completo_padrao_salvar_versao_historico_outbox_envio_e_conflito()
    {
        factory.Crivonft.Enviados.Clear();
        var admin = await factory.SeedAndLoginAdminAsync();
        var operador = await factory.CreateAndLoginAsync(admin, "operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.GetAsync("/api/precificacao")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.Put("/api/precificacao", Corpo(0))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/precificacao")).StatusCode);

        var atual = await (await admin.GetAsync("/api/precificacao")).Json();
        var v0 = atual.GetProperty("versao").GetInt32();
        if (v0 == 0)
        {
            // Padrões de negócio do crivonft quando ainda não há nada salvo.
            Assert.Equal(13m, atual.GetProperty("configuracao").GetProperty("valorKFeira").GetDecimal());
            Assert.Equal(4, atual.GetProperty("faixasPix").GetArrayLength());
        }

        // Validação
        var ruim = await admin.Put("/api/precificacao", Corpo(v0, mult: -1m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ruim.StatusCode);

        // Salvar sobe a versão, grava histórico e cria o envio pendente
        var salvo = await (await admin.Put("/api/precificacao", Corpo(v0, dolarVenda: 6.1m))).Json();
        Assert.Equal(v0 + 1, salvo.GetProperty("versao").GetInt32());
        Assert.Equal(6.1m, salvo.GetProperty("configuracao").GetProperty("dolarPadraoVenda").GetDecimal());
        Assert.Equal("pendente", salvo.GetProperty("ultimoEnvio").GetProperty("estado").GetString());
        Assert.Equal(v0 + 1, salvo.GetProperty("ultimoEnvio").GetProperty("versao").GetInt32());

        var hist = await (await admin.GetAsync("/api/precificacao/historico")).Json();
        var h = hist.GetProperty("items")[0];
        Assert.Equal(v0 + 1, h.GetProperty("versao").GetInt32());
        Assert.Equal(6.1m, h.GetProperty("depois").GetProperty("configuracao_calculo").GetProperty("dolar_padrao_venda").GetDecimal());
        Assert.Equal("painel", h.GetProperty("origem").GetString());
        Assert.Equal("Admin Teste", h.GetProperty("userNome").GetString());

        // Conflito: versão desatualizada
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Put("/api/precificacao", Corpo(v0))).StatusCode);

        // Segundo salvamento substitui o envio pendente do primeiro
        await admin.Put("/api/precificacao", Corpo(v0 + 1, dolarVenda: 6.2m));
        var envios = await (await admin.GetAsync("/api/precificacao/envios")).Json();
        var estados = envios.GetProperty("items").EnumerateArray().Take(2).Select(x => (x.GetProperty("versao").GetInt32(), x.GetProperty("estado").GetString())).ToList();
        Assert.Contains((v0 + 2, "pendente"), estados);
        Assert.Contains((v0 + 1, "substituido"), estados);

        // Worker: falha 500 -> continua pendente com backoff; depois sucesso
        factory.Crivonft.StatusDoPut = 500;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var envio = scope.ServiceProvider.GetRequiredService<PrecificacaoEnvioService>();
            Assert.Equal(0, await envio.ProcessarAsync(CancellationToken.None));
        }
        var depoisFalha = (await (await admin.GetAsync("/api/precificacao/envios")).Json()).GetProperty("items")[0];
        Assert.Equal("pendente", depoisFalha.GetProperty("estado").GetString());
        Assert.Equal(1, depoisFalha.GetProperty("tentativas").GetInt32());
        Assert.Equal(500, depoisFalha.GetProperty("ultimoStatusHttp").GetInt32());
        Assert.Contains("HTTP 500", depoisFalha.GetProperty("ultimoErro").GetString());

        // Ainda dentro do backoff: nada é reenviado
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<PrecificacaoEnvioService>().ProcessarAsync(CancellationToken.None));
        Assert.Single(factory.Crivonft.Enviados);

        // "Reenviar" zera o backoff
        Assert.Equal(HttpStatusCode.Accepted, (await admin.Post("/api/precificacao/reenviar")).StatusCode);
        factory.Crivonft.StatusDoPut = 200;
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<PrecificacaoEnvioService>().ProcessarAsync(CancellationToken.None));
        var enviado = (await (await admin.GetAsync("/api/precificacao/envios")).Json()).GetProperty("items")[0];
        Assert.Equal("enviado", enviado.GetProperty("estado").GetString());
        var corpoEnviado = JsonDocument.Parse(factory.Crivonft.Enviados[^1]).RootElement;
        Assert.Equal(v0 + 2, corpoEnviado.GetProperty("versao").GetInt32());
        Assert.Equal(6.2m, corpoEnviado.GetProperty("configuracao_calculo").GetProperty("dolar_padrao_venda").GetDecimal());
        Assert.Equal(2, corpoEnviado.GetProperty("faixas_pix").GetArrayLength());

        // Erro permanente (400) desiste sem repetir
        await admin.Put("/api/precificacao", Corpo(v0 + 2, dolarVenda: 6.3m));
        factory.Crivonft.StatusDoPut = 400;
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PrecificacaoEnvioService>().ProcessarAsync(CancellationToken.None);
        Assert.Equal("falhou", (await (await admin.GetAsync("/api/precificacao/envios")).Json()).GetProperty("items")[0].GetProperty("estado").GetString());
        factory.Crivonft.StatusDoPut = 200;

        // Cálculo pelas regras salvas (3 = 6,3 de câmbio; faixa 0..10 com multiplicador 6,5)
        var calc = await (await admin.GetAsync("/api/precificacao/calculo?usd=10")).Json();
        Assert.Equal(65m, calc.GetProperty("precoPixBrl").GetDecimal());
    }

    [Fact]
    public async Task Importar_do_crivonft_aplica_o_estado_remoto_sem_enfileirar_envio_e_nao_regride_a_versao()
    {
        var admin = await factory.SeedAndLoginAdminAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Teste isolado: zera o singleton para simular o "primeiro uso".
            await db.PrecificacaoEnvios.ExecuteDeleteAsync();
            await db.FaixasPix.ExecuteDeleteAsync();
            await db.PrecificacaoConfigs.ExecuteDeleteAsync();
        }
        factory.Crivonft.Remota = new PrecificacaoContrato.PayloadDto(7,
            new(14, 12, 1000, 5.5m, 5.9m, 2, 18),
            [new(0, 20, 7.1m, 0, true), new(20.01m, null, 6.4m, 1, true)]);

        var r = await (await admin.Post("/api/precificacao/importar-crivonft", new { })).Json();
        Assert.Equal(7, r.GetProperty("versao").GetInt32()); // adota a versão do crivonft (maior que a local 0)
        Assert.Equal(14m, r.GetProperty("configuracao").GetProperty("valorKFeira").GetDecimal());
        Assert.Equal(2, r.GetProperty("faixasPix").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("ultimoEnvio").ValueKind); // nada a enviar de volta
        var h = (await (await admin.GetAsync("/api/precificacao/historico")).Json()).GetProperty("items")[0];
        Assert.Equal("importacao_crivonft", h.GetProperty("origem").GetString());

        // Só na primeira vez: depois exige "forcar"
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post("/api/precificacao/importar-crivonft", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Post("/api/precificacao/importar-crivonft", new { forcar = true })).StatusCode);

        // O próximo salvamento do painel vem depois da versão do crivonft
        var atual = await (await admin.GetAsync("/api/precificacao")).Json();
        var salvo = await (await admin.Put("/api/precificacao", Corpo(atual.GetProperty("versao").GetInt32()))).Json();
        Assert.Equal(atual.GetProperty("versao").GetInt32() + 1, salvo.GetProperty("versao").GetInt32());
    }

    [Fact]
    public async Task Sem_configuracao_de_envio_a_importacao_responde_503()
    {
        using var semConfig = factory.WithWebHostBuilder(b => b.UseSetting("Integrations:Crivonft:HmacSecret", ""));
        await factory.SeedAndLoginAdminAsync("sem-config@teste.dev");
        var c = await semConfig.LoginComo("sem-config@teste.dev");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.Post("/api/precificacao/importar-crivonft", new { })).StatusCode);
    }
}
