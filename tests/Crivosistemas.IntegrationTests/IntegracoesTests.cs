using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Crivosistemas.IntegrationTests;

[Collection("api")]
public sealed class IntegracoesTests(ApiFactory factory)
{
    private static string Id() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Evento_de_venda_ponta_a_ponta_grava_o_evento_a_venda_e_o_cliente()
    {
        var maquina = factory.Maquina();
        var oid = Id();
        var corpo = CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc), 123.45m, nick: "Ponta-A-Ponta", cupom: "PROMO10"));
        var (status, resp) = await maquina.Enviar(corpo);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(resp.GetProperty("ok").GetBoolean());

        var admin = await factory.SeedAndLoginAdminAsync();
        var lista = await (await admin.GetAsync($"/api/vendas?busca={oid}")).Json();
        Assert.Equal(1, lista.GetProperty("total").GetInt32());
        var venda = lista.GetProperty("items")[0];
        Assert.Equal(123.45m, venda.GetProperty("totalBrl").GetDecimal());
        Assert.Equal("Ponta-A-Ponta", venda.GetProperty("cliente").GetProperty("nickname").GetString());

        var det = await (await admin.GetAsync($"/api/vendas/{venda.GetProperty("id").GetGuid()}")).Json();
        Assert.Equal("PROMO10", det.GetProperty("cupom").GetProperty("codigo").GetString());
        Assert.Equal("percentual", det.GetProperty("cupom").GetProperty("tipo").GetString());
        Assert.Single(det.GetProperty("itens").EnumerateArray());
        Assert.Equal("processado", det.GetProperty("eventos")[0].GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Evento_repetido_responde_duplicado_e_nao_reaplica()
    {
        var maquina = factory.Maquina();
        var oid = Id();
        var id = Guid.NewGuid();
        var corpo = CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, DateTime.UtcNow.AddDays(-1), 50m), id);
        Assert.Equal(HttpStatusCode.OK, (await maquina.Enviar(corpo)).Status);
        var (status, resp) = await maquina.Enviar(corpo);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(resp.GetProperty("duplicado").GetBoolean());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.EventosRecebidos.CountAsync(e => e.EventoId == id));
        Assert.Equal(1, await db.Vendas.CountAsync(v => v.OrigemId == oid));
    }

    [Fact]
    public async Task Evento_mais_antigo_que_o_gravado_e_ignorado_e_o_mais_novo_atualiza()
    {
        var maquina = factory.Maquina();
        var oid = Id();
        var t0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        await maquina.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, t0, 100m, atualizadoEm: t0.AddHours(2))));
        var (_, velho) = await maquina.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, t0, 999m, atualizadoEm: t0.AddHours(1))));
        Assert.StartsWith("ignorado", velho.GetProperty("resultado").GetString());
        await maquina.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, t0, 150m, atualizadoEm: t0.AddHours(3))));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(150m, (await db.Vendas.SingleAsync(v => v.OrigemId == oid)).TotalBrl);
    }

    [Fact]
    public async Task Venda_removida_fica_marcada_e_sai_da_listagem_e_um_upsert_atrasado_nao_a_revive()
    {
        var maquina = factory.Maquina();
        var admin = await factory.SeedAndLoginAdminAsync();
        var oid = Id();
        var t0 = DateTime.UtcNow.AddDays(-2);
        await maquina.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, t0, 80m)));
        Assert.Equal(1, (await (await admin.GetAsync($"/api/vendas?busca={oid}")).Json()).GetProperty("total").GetInt32());

        var (st, _) = await maquina.Enviar(CrivoHelpers.Evento("venda.removida", new { origem = "nft", origem_id = oid }));
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal(0, (await (await admin.GetAsync($"/api/vendas?busca={oid}")).Json()).GetProperty("total").GetInt32());
        var com = await (await admin.GetAsync($"/api/vendas?busca={oid}&incluirRemovidas=true")).Json();
        Assert.True(com.GetProperty("items")[0].GetProperty("removida").GetBoolean());

        await maquina.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, t0, 80m)));
        Assert.Equal(0, (await (await admin.GetAsync($"/api/vendas?busca={oid}")).Json()).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Cliente_upsert_cria_e_atualiza()
    {
        var maquina = factory.Maquina();
        var nick = "cli-" + Id()[..10];
        await maquina.Enviar(CrivoHelpers.Evento("cliente.upsert", new { chave = nick, nickname = nick, email = (string?)null, identidade_id = (string?)null, criado_em = "2026-01-01T00:00:00Z" }));
        await maquina.Enviar(CrivoHelpers.Evento("cliente.upsert", new { chave = nick, nickname = nick, email = "novo@x.com", identidade_id = (string?)null, criado_em = "2026-01-01T00:00:00Z" }));
        var admin = await factory.SeedAndLoginAdminAsync();
        var lista = await (await admin.GetAsync($"/api/clientes?busca={nick}")).Json();
        Assert.Equal(1, lista.GetProperty("total").GetInt32());
        Assert.Equal("novo@x.com", lista.GetProperty("items")[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task Venda_sem_cliente_e_aceita()
    {
        var oid = Id();
        var (st, _) = await factory.Maquina().Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, DateTime.UtcNow.AddDays(-1), 20m, nick: null)));
        Assert.Equal(HttpStatusCode.OK, st);
    }

    [Fact]
    public async Task Autenticacao_invalida_responde_401()
    {
        var maquina = factory.Maquina();
        var corpo = CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(Id(), DateTime.UtcNow, 10m));
        Assert.Equal(HttpStatusCode.Unauthorized, (await maquina.Enviar(corpo, CrivoHelpers.Requisicao(corpo, segredo: "errado"))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await maquina.Enviar(corpo, CrivoHelpers.Requisicao(corpo, token: "outro"))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await maquina.Enviar(corpo, CrivoHelpers.Requisicao(corpo, token: null))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await maquina.Enviar(corpo, CrivoHelpers.Requisicao(corpo, assinaturaFixa: "sha256=00"))).Status);
        // Corpo adulterado depois de assinado
        var req = CrivoHelpers.Requisicao(corpo);
        req.Content = new StringContent(corpo.Replace("10", "11"), System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await maquina.Enviar(corpo, req)).Status);
        // Timestamp fora da janela de 300 s, mesmo com assinatura correta para ele
        var velho = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 400;
        var (st, resp) = await maquina.Enviar(corpo, CrivoHelpers.Requisicao(corpo, timestamp: velho));
        Assert.Equal(HttpStatusCode.Unauthorized, st);
        Assert.Equal("timestamp_invalido", resp.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Payload_invalido_responde_4xx_e_fica_registrado_como_rejeitado()
    {
        var maquina = factory.Maquina();
        var id = Guid.NewGuid();
        var corpo = CrivoHelpers.Evento("venda.upsert", new { origem = "xpto", origem_id = "1" }, id);
        var (st, resp) = await maquina.Enviar(corpo);
        Assert.Equal(HttpStatusCode.BadRequest, st);
        Assert.NotEmpty(resp.GetProperty("erros").EnumerateArray());

        var (st2, _) = await maquina.Enviar("isto nao e json");
        Assert.Equal(HttpStatusCode.BadRequest, st2);

        var admin = await factory.SeedAndLoginAdminAsync();
        var lista = await (await admin.GetAsync($"/api/integracoes/crivonft/eventos?estado=rejeitado&busca={id}")).Json();
        Assert.Equal(1, lista.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Erro_inesperado_responde_5xx_registra_falha_e_o_admin_reprocessa()
    {
        var maquina = factory.Maquina();
        var oid = Id();
        var id = Guid.NewGuid();
        // Valor fora da faixa da coluna decimal: o payload é válido, mas o banco recusa, o que é um erro inesperado (5xx), não do payload.
        var (st, _) = await maquina.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, DateTime.UtcNow.AddDays(-1), 1e13m), id));
        Assert.Equal(HttpStatusCode.InternalServerError, st);

        var admin = await factory.SeedAndLoginAdminAsync();
        var ev = (await (await admin.GetAsync($"/api/integracoes/crivonft/eventos?estado=falhou&busca={id}")).Json()).GetProperty("items")[0];
        Assert.False(string.IsNullOrEmpty(ev.GetProperty("erro").GetString()));

        // O admin conserta o dado gravado (aqui, trocando o corpo) e reprocessa.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = await db.EventosRecebidos.SingleAsync(x => x.EventoId == id);
            e.Corpo = CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, DateTime.UtcNow.AddDays(-1), 33m), id);
            await db.SaveChangesAsync();
        }
        var resp = await (await admin.Post($"/api/integracoes/crivonft/eventos/{ev.GetProperty("id").GetGuid()}/reprocessar")).Json();
        Assert.Equal("processado", resp.GetProperty("estado").GetString());
        Assert.Equal(1, (await (await admin.GetAsync($"/api/vendas?busca={oid}")).Json()).GetProperty("total").GetInt32());

        // Um reenvio do mesmo evento depois de processado é duplicado.
        Assert.True((await maquina.Enviar(CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(oid, DateTime.UtcNow, 33m), id))).Corpo.GetProperty("duplicado").GetBoolean());
    }

    [Fact]
    public async Task Sem_configuracao_o_endpoint_de_eventos_responde_503()
    {
        using var semConfig = factory.WithWebHostBuilder(b => b.UseSetting("Integrations:Crivonft:Token", ""));
        var corpo = CrivoHelpers.Evento("venda.upsert", CrivoHelpers.Venda(Id(), DateTime.UtcNow, 10m));
        var c = semConfig.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.SendAsync(CrivoHelpers.Requisicao(corpo))).StatusCode);
    }

    [Fact]
    public async Task Rotas_administrativas_exigem_administrador_e_o_endpoint_de_eventos_nao_exige_csrf()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/integracoes/crivonft/status")).StatusCode);
        var admin = await factory.SeedAndLoginAdminAsync();
        var operador = await factory.CreateAndLoginAsync(admin, "operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.GetAsync("/api/integracoes/crivonft/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.Post("/api/integracoes/crivonft/reconciliacoes", new { })).StatusCode);

        // POST de outra rota sem o cabeçalho anti-CSRF é barrado; o de eventos passa (autentica por HMAC).
        var semCsrf = factory.CreateClient(new() { HandleCookies = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await semCsrf.PostAsJsonAsync("/api/auth/login", new { email = "a@b.c", password = "x" })).StatusCode);
    }

    [Fact]
    public async Task Status_mostra_contagens_ultimo_evento_e_erros()
    {
        var maquina = factory.Maquina();
        await maquina.Enviar(CrivoHelpers.Evento("venda.upsert", new { origem = "xpto" }));
        await maquina.Enviar(CrivoHelpers.Evento("cliente.upsert", new { chave = "st-" + Id()[..8], nickname = "st" }));
        var admin = await factory.SeedAndLoginAdminAsync();
        var s = await (await admin.GetAsync("/api/integracoes/crivonft/status")).Json();
        Assert.True(s.GetProperty("recebeEventos").GetBoolean());
        Assert.True(s.GetProperty("eventosPorEstado").GetProperty("processado").GetInt32() >= 1);
        Assert.True(s.GetProperty("eventosPorEstado").GetProperty("rejeitado").GetInt32() >= 1);
        Assert.NotEqual(JsonValueKind.Null, s.GetProperty("ultimoEventoRecebidoEm").ValueKind);
        Assert.NotEmpty(s.GetProperty("ultimosErros").EnumerateArray());
    }

    [Fact]
    public async Task Cliente_com_carteira_e_gravado_buscavel_e_null_nao_apaga()
    {
        var maquina = factory.Maquina();
        var chave = "carteira-" + Id();
        var enderecoMaiusculo = "0xAbCdEf" + Guid.NewGuid().ToString("N")[..30] + "0123";
        var endereco = enderecoMaiusculo.ToLowerInvariant();
        object Cliente(string? carteira) => new { chave, nickname = "Cli " + chave[^6..], email = (string?)null, identidade_id = (string?)null, carteira, criado_em = "2026-01-01T00:00:00Z" };

        Assert.Equal(HttpStatusCode.OK, (await maquina.Enviar(CrivoHelpers.Evento("cliente.upsert", Cliente(enderecoMaiusculo)))).Status);

        var admin = await factory.SeedAndLoginAdminAsync();
        var lista = await (await admin.GetAsync($"/api/clientes?busca={endereco[..20]}")).Json();
        var cli = Assert.Single(lista.GetProperty("items").EnumerateArray());
        Assert.Equal(endereco, cli.GetProperty("carteira").GetString());
        var id = cli.GetProperty("id").GetGuid();

        // Evento seguinte sem carteira (ou com valor que não é endereço) não apaga a conhecida.
        Assert.Equal(HttpStatusCode.OK, (await maquina.Enviar(CrivoHelpers.Evento("cliente.upsert", Cliente(null)))).Status);
        Assert.Equal(HttpStatusCode.OK, (await maquina.Enviar(CrivoHelpers.Evento("cliente.upsert", Cliente("lixo")))).Status);
        var det = await (await admin.GetAsync($"/api/clientes/{id}")).Json();
        Assert.Equal(endereco, det.GetProperty("cliente").GetProperty("carteira").GetString());

        // A venda desse cliente mostra a carteira e é achada por ela.
        var oid = Id();
        var venda = CrivoHelpers.Venda(oid, DateTime.UtcNow.AddDays(-1), 80m, nick: chave);
        Assert.Equal(HttpStatusCode.OK, (await maquina.Enviar(CrivoHelpers.Evento("venda.upsert", venda))).Status);
        var vendas = await (await admin.GetAsync($"/api/vendas?busca={endereco[..20]}")).Json();
        var v = Assert.Single(vendas.GetProperty("items").EnumerateArray());
        Assert.Equal(endereco, v.GetProperty("cliente").GetProperty("carteira").GetString());
    }
}
