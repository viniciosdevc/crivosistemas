using System.Text.Json;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Vendas;

namespace Crivosistemas.UnitTests;

public class VendaEnvioTests
{
    [Fact]
    public void ExternalId_leva_o_prefixo_dash()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"dash:{id}", VendaEnvioRegras.ExternalId(id));
    }

    [Theory]
    [InlineData(StatusVenda.Pendente, "pendente")]
    [InlineData(StatusVenda.Concluido, "entregue")]
    [InlineData(StatusVenda.Cancelado, "cancelado")]
    public void Status_vira_vocabulario_do_crivonft(StatusVenda s, string esperado) => Assert.Equal(esperado, VendaEnvioRegras.StatusCrivonft(s));

    [Fact]
    public void Custo_em_cambios_vira_manual_no_crivonft() => Assert.Equal("manual", VendaEnvioRegras.OrigemCustoCrivonft(OrigemCusto.Cambios));

    [Fact]
    public void Corpo_tem_chave_externa_e_valores_prontos()
    {
        var v = new Venda
        {
            Origem = OrigemVenda.Manual, OrigemId = "x", DataHora = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc), AtualizadoEmOrigem = new DateTime(2026, 10, 24, 13, 0, 0, DateTimeKind.Utc),
            StatusNormalizado = StatusVenda.Concluido, FormaPagamento = FormaPagamento.Pix, TotalBrl = 50m, LucroBrl = 10m, CustoUsd = 6.5m, OrigemCusto = OrigemCusto.EstoqueUsd,
            Cliente = new Cliente { Chave = "ana", Nickname = "Ana" },
        };
        v.Itens.Add(new VendaItem { VendaId = v.Id, Descricao = "Item X", Quantidade = 1 });
        var c = VendaEnvioRegras.Corpo(v);
        Assert.Equal($"dash:{v.Id}", c["external_id"]);
        Assert.Equal("entregue", c["status"]);
        Assert.Equal("Ana", c["nickname_comprador"]);
        Assert.Equal("estoque_usd", c["origem_custo"]);
        Assert.Equal(50m, c["receita_total_brl"]);
        Assert.Contains("\"atualizado_em\":\"2026-10-24T13:00:00.0000000Z\"", JsonSerializer.Serialize(c));
    }

    [Fact]
    public void Importacao_ignora_espelho_de_venda_do_proprio_sistema()
    {
        using var doc = JsonDocument.Parse("""{"id":"a1","external_id":"dash:abc","data_hora":"2026-10-24T12:00:00Z","status":"entregue","receita_total_brl":10}""");
        var r = ImportacaoMapper.Venda(doc.RootElement, out var ignorada);
        Assert.Null(r.Valor);
        Assert.NotNull(ignorada);
    }
}
