using System.Text;
using System.Text.Json;
using Crivosistemas.Domain.Integracoes;
using Crivosistemas.Domain.Precificacao;
using Crivosistemas.Domain.Vendas;

namespace Crivosistemas.UnitTests;

public sealed class CrivoAuthTests
{
    private const string Token = "tok";
    private const string Segredo = "segredo";
    private static readonly byte[] Corpo = Encoding.UTF8.GetBytes("""{"id":"x","dados":{"nome":"ação"}}""");

    [Fact]
    public void Assinatura_e_hmac_sha256_de_timestamp_ponto_corpo_em_hex_minusculo()
    {
        // Valor de referência calculado de forma independente: HMAC-SHA256("segredo", "1700000000.{...}")
        var esperado = "sha256=" + Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Segredo), Encoding.UTF8.GetBytes("1700000000." + Encoding.UTF8.GetString(Corpo))));
        Assert.Equal(esperado, CrivoAuth.Assinar(Segredo, "1700000000", Corpo));
        Assert.Equal(esperado, CrivoAuth.Assinar(Segredo, "1700000000", Encoding.UTF8.GetString(Corpo)));
        Assert.Matches("^sha256=[0-9a-f]{64}$", esperado);
    }

    private static ResultadoAuth Verificar(string? auth = "Bearer " + Token, string ts = "1700000000", string? assinatura = null, long agora = 1700000000, byte[]? corpo = null)
    {
        corpo ??= Corpo;
        return CrivoAuth.Verificar(Token, Segredo, auth, ts, assinatura ?? CrivoAuth.Assinar(Segredo, ts, Corpo), corpo, agora);
    }

    [Fact]
    public void Aceita_credenciais_corretas_dentro_da_janela()
    {
        Assert.Equal(ResultadoAuth.Ok, Verificar());
        Assert.Equal(ResultadoAuth.Ok, Verificar(agora: 1700000000 + 300));
        Assert.Equal(ResultadoAuth.Ok, Verificar(agora: 1700000000 - 300));
        Assert.Equal(ResultadoAuth.Ok, Verificar(auth: "bearer " + Token));
    }

    [Fact]
    public void Recusa_timestamp_fora_da_janela_de_300_segundos()
    {
        Assert.Equal(ResultadoAuth.TimestampInvalido, Verificar(agora: 1700000000 + 301));
        Assert.Equal(ResultadoAuth.TimestampInvalido, Verificar(agora: 1700000000 - 301));
        Assert.Equal(ResultadoAuth.TimestampInvalido, Verificar(ts: "abc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer outro")]
    [InlineData("Basic tok")]
    [InlineData("tok")]
    public void Recusa_bearer_ausente_ou_errado(string? auth) => Assert.Equal(ResultadoAuth.NaoAutorizado, Verificar(auth: auth));

    [Fact]
    public void Recusa_assinatura_errada_corpo_adulterado_ou_segredo_diferente()
    {
        Assert.Equal(ResultadoAuth.NaoAutorizado, Verificar(assinatura: "sha256=abc"));
        Assert.Equal(ResultadoAuth.NaoAutorizado, Verificar(assinatura: ""));
        Assert.Equal(ResultadoAuth.NaoAutorizado, Verificar(corpo: Encoding.UTF8.GetBytes("{}")));
        Assert.Equal(ResultadoAuth.NaoAutorizado, Verificar(assinatura: CrivoAuth.Assinar("outro", "1700000000", Corpo)));
        // A assinatura vale para o timestamp assinado: trocar o timestamp invalida
        Assert.Equal(ResultadoAuth.NaoAutorizado, Verificar(ts: "1700000001", assinatura: CrivoAuth.Assinar(Segredo, "1700000000", Corpo)));
    }

    [Fact]
    public void Assinatura_em_maiusculas_e_aceita_e_a_comparacao_ignora_tamanho()
    {
        Assert.Equal(ResultadoAuth.Ok, Verificar(assinatura: CrivoAuth.Assinar(Segredo, "1700000000", Corpo).ToUpperInvariant().Replace("SHA256=", "sha256=")));
        Assert.True(CrivoAuth.IguaisEmTempoConstante("a", "a"));
        Assert.False(CrivoAuth.IguaisEmTempoConstante("a", "aa"));
        Assert.False(CrivoAuth.IguaisEmTempoConstante(null, "a"));
    }
}

public sealed class PayloadParserTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private const string VendaValida = """
    {"origem":"site","origem_id":"o1","pedido_id":"p1","data_hora":"2026-10-23T18:00:00Z","status":"paid","status_normalizado":"pago","forma_pagamento":"hibrido",
     "vendedor":null,"cliente":{"chave":"Ana","nickname":"Ana","email":"a@x.com","identidade_id":null},
     "itens":[{"descricao":"Item","quantidade":2,"preco_usd":"10.5","preco_brl":63.123,"referencia":null}],
     "valores":{"total_brl":126.246,"desconto_brl":0,"taxa_brl":1,"valor_pix_brl":100,"valor_cambios":2.5,"custo_brl":50,"custo_usd":8,"custo_cambios":1,"lucro_brl":76.25,"margem_percent":60.3},
     "cupom":{"codigo":"X10","tipo":"valor_fixo"},"atualizado_em":"2026-10-23T18:05:00Z"}
    """;

    [Fact]
    public void Mapeia_uma_venda_completa()
    {
        var r = PayloadParser.VendaUpsert(J(VendaValida));
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        var v = r.Valor!;
        Assert.Equal(OrigemVenda.Site, v.Origem);
        Assert.Equal("o1", v.OrigemId);
        Assert.Equal(StatusVenda.Pago, v.StatusNormalizado);
        Assert.Equal(FormaPagamento.Hibrido, v.FormaPagamento);
        Assert.Equal("Ana", v.Cliente!.Chave);
        Assert.Equal(new DateTime(2026, 10, 23, 18, 5, 0, DateTimeKind.Utc), v.AtualizadoEm);
        Assert.Equal(DateTimeKind.Utc, v.DataHora.Kind);
        Assert.Equal(2, v.Itens[0].Quantidade);
        Assert.Equal(10.5m, v.Itens[0].PrecoUsd);   // número em texto é aceito
        Assert.Equal(63.12m, v.Itens[0].PrecoBrl);  // BRL arredondado a 2 casas
        Assert.Equal(126.25m, v.Valores.TotalBrl);
        Assert.Equal(2.5m, v.Valores.ValorCambios); // câmbios não é BRL: mantém a precisão
        Assert.Equal(TipoCupom.ValorFixo, v.Cupom!.Tipo);
    }

    [Fact]
    public void Cliente_e_cupom_nulos_sao_aceitos()
    {
        var json = VendaValida.Replace(""""cliente":{"chave":"Ana","nickname":"Ana","email":"a@x.com","identidade_id":null},"""", """"cliente":null,"""").Replace(""""cupom":{"codigo":"X10","tipo":"valor_fixo"}"""", """"cupom":null"""");
        var r = PayloadParser.VendaUpsert(J(json));
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        Assert.Null(r.Valor!.Cliente);
        Assert.Null(r.Valor.Cupom);
    }

    [Fact]
    public void Acumula_todos_os_erros()
    {
        var r = PayloadParser.VendaUpsert(J("""{"origem":"xpto","status_normalizado":"?","data_hora":"ontem","valores":{"total_brl":"abc"}}"""));
        Assert.False(r.Ok);
        Assert.Contains(r.Erros, e => e.Contains("origem"));
        Assert.Contains(r.Erros, e => e.Contains("origem_id"));
        Assert.Contains(r.Erros, e => e.Contains("status_normalizado"));
        Assert.Contains(r.Erros, e => e.Contains("data_hora"));
        Assert.Contains(r.Erros, e => e.Contains("total_brl"));
        Assert.Contains(r.Erros, e => e.Contains("atualizado_em"));
    }

    [Theory]
    [InlineData("""{"id":"3f2b6f43-3f0b-4f37-8f5a-0d2b1d2f9c11","tipo":"venda.upsert","ocorrido_em":"2026-10-23T18:00:00Z","dados":{}}""", true)]
    [InlineData("""{"id":"nao-uuid","tipo":"venda.upsert","ocorrido_em":"2026-10-23T18:00:00Z","dados":{}}""", false)]
    [InlineData("""{"id":"3f2b6f43-3f0b-4f37-8f5a-0d2b1d2f9c11","tipo":"outro","ocorrido_em":"2026-10-23T18:00:00Z","dados":{}}""", false)]
    [InlineData("""{"id":"3f2b6f43-3f0b-4f37-8f5a-0d2b1d2f9c11","tipo":"cliente.upsert","dados":{}}""", false)]
    [InlineData("""{"id":"3f2b6f43-3f0b-4f37-8f5a-0d2b1d2f9c11","tipo":"cliente.upsert","ocorrido_em":"2026-10-23T18:00:00Z","dados":[]}""", false)]
    [InlineData("[]", false)]
    [InlineData("nada", false)]
    public void Valida_o_envelope(string json, bool ok) => Assert.Equal(ok, PayloadParser.Envelope(json).Ok);

    [Fact]
    public void Remocao_e_cliente()
    {
        Assert.True(PayloadParser.VendaRemovida(J("""{"origem":"nft","origem_id":"1"}""")).Ok);
        Assert.False(PayloadParser.VendaRemovida(J("""{"origem":"nft"}""")).Ok);
        var c = PayloadParser.ClienteUpsert(J("""{"chave":"k","nickname":"Nick","criado_em":"2026-01-01T00:00:00Z"}"""));
        Assert.True(c.Ok);
        Assert.Equal("Nick", c.Valor!.Nickname);
        Assert.Equal("k", PayloadParser.ClienteUpsert(J("""{"chave":"k"}""")).Valor!.Nickname); // nickname cai para a chave
        Assert.False(PayloadParser.ClienteUpsert(J("{}")).Ok);
    }

    [Fact]
    public void Carteira_do_cliente_e_normalizada_e_opcional()
    {
        var end = "0xAbCdEf0123456789abcdef0123456789ABCDEF01";
        Assert.Equal(end.ToLowerInvariant(), PayloadParser.ClienteUpsert(J($$"""{"chave":"k","carteira":"{{end}}"}""")).Valor!.Carteira);
        Assert.Null(PayloadParser.ClienteUpsert(J("""{"chave":"k"}""")).Valor!.Carteira);
        Assert.Null(PayloadParser.ClienteUpsert(J("""{"chave":"k","carteira":null}""")).Valor!.Carteira);
        Assert.Equal("0x123", PayloadParser.ClienteUpsert(J("""{"chave":"k","carteira":"0x123"}""")).Valor!.Carteira); // campo livre: mantida, só não é válida
        var vendaJson = """{"origem":"nft","origem_id":"1","data_hora":"2026-01-01T00:00:00Z","status":"x","status_normalizado":"concluido","atualizado_em":"2026-01-01T00:00:00Z","cliente":{"chave":"k","carteira":"CART"},"itens":[],"valores":{}}""".Replace("CART", end);
        Assert.Equal(end.ToLowerInvariant(), PayloadParser.VendaUpsert(J(vendaJson)).Valor?.Cliente?.Carteira);
        Assert.Equal(end.ToLowerInvariant(), ImportacaoMapper.Cliente(J($$"""{"nickname":"ana","carteira":"{{end}}"}""")).Valor!.Carteira);
        Assert.Equal("texto livre", ImportacaoMapper.Cliente(J("""{"nickname":"ana","carteira":" texto livre "}""")).Valor!.Carteira);
        Assert.True(ImportacaoMapper.Cliente(J("""{"nickname":"ana","favorito":true}""")).Valor!.Favorito);
        Assert.Null(ImportacaoMapper.Cliente(J("""{"nickname":"ana"}""")).Valor!.Favorito);
    }

    [Fact]
    public void Carteira_livre_favorito_e_carteira_da_loja_no_evento_de_cliente()
    {
        var lojaMaiuscula = "0xAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var p = PayloadParser.ClienteUpsert(J($$"""{"chave":"k","carteira":"  minha carteira  ","favorito":true,"carteira_loja":"{{lojaMaiuscula}}"}""")).Valor!;
        Assert.Equal("minha carteira", p.Carteira);
        Assert.True(p.Favorito);
        Assert.Equal(lojaMaiuscula.ToLowerInvariant().Replace("0x", "0x"), p.CarteiraLoja);
        // Ausente ou null: Favorito null (não apaga); carteira_loja que não é endereço é ignorada.
        var vazio = PayloadParser.ClienteUpsert(J("""{"chave":"k","favorito":null,"carteira_loja":"lixo"}""")).Valor!;
        Assert.Null(vazio.Favorito);
        Assert.Null(vazio.CarteiraLoja);
        Assert.False(PayloadParser.ClienteUpsert(J("""{"chave":"k","favorito":false}""")).Valor!.Favorito);
        // Texto acima de 120 é cortado, não rejeitado.
        Assert.Equal(120, PayloadParser.ClienteUpsert(J($$"""{"chave":"k","carteira":"{{new string('x', 200)}}"}""")).Valor!.Carteira!.Length);
    }

    [Theory]
    [InlineData("0xAbCdEf0123456789abcdef0123456789ABCDEF01", true)]
    [InlineData("0x123", false)]
    [InlineData("texto livre", false)]
    [InlineData("0xGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG", false)]
    [InlineData(null, false)]
    public void Carteira_valida_exige_0x_e_40_hexadecimais(string? valor, bool esperado) => Assert.Equal(esperado, PayloadParser.CarteiraValida(valor));

    [Fact]
    public void Texto_de_carteira_faz_trim_corta_em_120_e_so_minuscula_endereco_valido()
    {
        Assert.Null(PayloadParser.TextoCarteira("   "));
        Assert.Equal("Abc DEF", PayloadParser.TextoCarteira("  Abc DEF "));
        Assert.Equal("0x" + new string('a', 40), PayloadParser.TextoCarteira("0x" + new string('A', 40)));
    }
}

public sealed class RegraUpsertTests
{
    private static readonly DateTime T = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Sem_gravado_aplica() => Assert.True(RegraUpsert.Aplicar(null, T));
    [Fact]
    public void Mais_novo_aplica() => Assert.True(RegraUpsert.Aplicar(T, T.AddSeconds(1)));
    [Fact]
    public void Igual_reaplica_de_forma_idempotente() => Assert.True(RegraUpsert.Aplicar(T, T));
    [Fact]
    public void Mais_antigo_e_ignorado() => Assert.False(RegraUpsert.Aplicar(T, T.AddSeconds(-1)));
}

public sealed class ImportacaoMapperTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("entregue", StatusVenda.Concluido)]
    [InlineData("Entregue", StatusVenda.Concluido)]
    [InlineData("cancelado", StatusVenda.Cancelado)]
    [InlineData("pendente", StatusVenda.Pendente)]
    [InlineData(null, StatusVenda.Pendente)]
    [InlineData("qualquer", StatusVenda.Pendente)]
    public void Status_do_nft(string? bruto, StatusVenda esperado) => Assert.Equal(esperado, ImportacaoMapper.StatusNft(bruto));

    [Fact]
    public void Mapeia_linha_exportada_e_ignora_espelho_de_pedido()
    {
        var ok = ImportacaoMapper.Venda(J("""{"id":"v1","pedido_id":null,"data_hora":"2026-05-01T15:00:00Z","status":"entregue","nickname_comprador":"ana","nome_item":"X","receita_total_brl":100,"custo_brl":40,"lucro_brl":60,"margem_percent":60,"valor_venda_pix":100,"forma_pagamento":"pix"}"""), out var ign);
        Assert.Null(ign);
        Assert.True(ok.Ok, string.Join(";", ok.Erros));
        Assert.Equal(OrigemVenda.Nft, ok.Valor!.Origem);
        Assert.Equal(StatusVenda.Concluido, ok.Valor.StatusNormalizado);
        Assert.Equal(100m, ok.Valor.Valores.TotalBrl);
        Assert.Equal(ok.Valor.DataHora, ok.Valor.AtualizadoEm); // sem atualizado_em usa a data da venda

        ImportacaoMapper.Venda(J("""{"id":"v2","pedido_id":"abc","data_hora":"2026-05-01T15:00:00Z"}"""), out var espelho);
        Assert.NotNull(espelho);
    }
}

public sealed class FusoSpTests
{
    [Fact]
    public void Dia_de_sao_paulo_usa_utc_menos_3()
    {
        Assert.Equal(new DateOnly(2026, 10, 22), FusoSp.Dia(new DateTime(2026, 10, 23, 2, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 10, 23), FusoSp.Dia(new DateTime(2026, 10, 23, 3, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Intervalo_cobre_os_dias_inclusivos()
    {
        var (ini, fim) = FusoSp.Intervalo(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 11));
        Assert.Equal(new DateTime(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc), ini);
        Assert.Equal(new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc), fim);
    }
}

public sealed class CalculoPrecoTests
{
    // Valores conferidos à mão com a fórmula do crivonft (src/lib/calculator.ts) para a configuração padrão:
    // valorK=13, dólar de venda=6, moedas/K=1000, taxa de prata=3 -> piso = usd*6 + 0,039.
    private static CalculoPixResultado Calc(double usd) => CalculoPreco.PrecoPix(usd, ConfigCalculo.Padrao, CalculoPreco.FaixasPadrao);

    [Theory]
    [InlineData(5, 31.00, 30.04, false)]   // faixa 0-10 x6,2 = 31; piso 30,04
    [InlineData(10, 62.00, 60.04, false)]
    [InlineData(12, 72.04, 72.04, true)]   // faixa x6 = 72, piso 72,039 -> 72,04: o piso vence por 4 centavos
    [InlineData(20, 120.04, 120.04, true)]
    [InlineData(30, 180.04, 180.04, true)] // faixa x5,9 = 177 abaixo do piso 180,04
    public void Preco_pix_replica_o_crivonft(double usd, double final, double piso, bool ajustado)
    {
        var r = Calc(usd);
        Assert.Equal(final, r.PrecoPixBrl);
        Assert.Equal(piso, r.PisoBrl);
        Assert.Equal(ajustado, r.AjustadoPeloPiso);
    }

    [Fact]
    public void Faixa_e_escolhida_pelo_valor_e_o_ultimo_degrau_vale_acima_de_todas()
    {
        Assert.Equal(6.2, CalculoPreco.EncontrarFaixa(10, CalculoPreco.FaixasPadrao).Multiplicador);
        Assert.Equal(6, CalculoPreco.EncontrarFaixa(10.01, CalculoPreco.FaixasPadrao).Multiplicador);
        Assert.Equal(5.9, CalculoPreco.EncontrarFaixa(10_000, CalculoPreco.FaixasPadrao).Multiplicador);
        // Buraco entre faixas: cai na última por valor mínimo
        var buraco = new[] { new FaixaCalculo(0, 5, 7), new FaixaCalculo(8, null, 6) };
        Assert.Equal(6, CalculoPreco.EncontrarFaixa(6, buraco).Multiplicador);
    }

    [Fact]
    public void Faixas_inativas_sao_ignoradas_e_sem_nenhuma_ativa_usa_as_padrao()
    {
        var faixas = new[] { new FaixaCalculo(0, null, 9, 0, Ativo: false), new FaixaCalculo(0, null, 8, 1, Ativo: true) };
        Assert.Equal(8, CalculoPreco.EncontrarFaixa(3, faixas).Multiplicador);
        Assert.Equal(6.2, CalculoPreco.EncontrarFaixa(3, [new FaixaCalculo(0, null, 9, 0, Ativo: false)]).Multiplicador);
    }

    [Fact]
    public void Configuracao_propria_muda_o_piso_e_o_preco()
    {
        var cfg = ConfigCalculo.Padrao with { DolarPadraoVenda = 7 }; // piso = 7*usd + 0,039
        var r = CalculoPreco.PrecoPix(10, cfg, CalculoPreco.FaixasPadrao);
        Assert.Equal(70.04, r.PrecoPixBrl);
        Assert.True(r.AjustadoPeloPiso);
    }

    [Theory]
    [InlineData(1.005, 1)] // 1,005*100 = 100,49999999999999 em ponto flutuante, igual ao JS
    [InlineData(2.675, 2.68)]
    [InlineData(0.004, 0)]
    [InlineData(double.NaN, 0)]
    public void Arredonda_como_math_round_do_javascript(double v, double esperado) => Assert.Equal(esperado, CalculoPreco.Arredondar(v));
}

public sealed class PrecificacaoContratoTests
{
    private static PrecificacaoContrato.ConfigDto Cfg(decimal valorK = 13, decimal moedas = 1000) => new(valorK, 12, moedas, 5.7m, 6, 3, 20);

    [Fact]
    public void Serializa_em_snake_case_como_no_contrato()
    {
        var json = PrecificacaoContrato.Serializar(new(3, Cfg(), [new(0, 10, 6.2m, 0, true), new(10.01m, null, 6, 1, true)]));
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        Assert.Equal(3, raiz.GetProperty("versao").GetInt32());
        Assert.Equal(13m, raiz.GetProperty("configuracao_calculo").GetProperty("valor_k_feira").GetDecimal());
        Assert.Equal(5.7m, raiz.GetProperty("configuracao_calculo").GetProperty("dolar_padrao_compra").GetDecimal());
        var faixa = raiz.GetProperty("faixas_pix")[1];
        Assert.Equal(JsonValueKind.Null, faixa.GetProperty("valor_max_usd").ValueKind);
        Assert.True(faixa.GetProperty("ativo").GetBoolean());
        Assert.Equal(3, PrecificacaoContrato.Ler(json)!.Versao);
    }

    [Fact]
    public void Validacao_espelha_as_regras_do_crivonft()
    {
        Assert.Empty(PrecificacaoContrato.Validar(Cfg(), [new(0, 10, 6, 0, true)]));
        Assert.NotEmpty(PrecificacaoContrato.Validar(Cfg(moedas: 0), []));
        Assert.NotEmpty(PrecificacaoContrato.Validar(Cfg(valorK: 0), []));
        Assert.NotEmpty(PrecificacaoContrato.Validar(Cfg(), [new(0, 10, 0, 0, true)]));
        Assert.NotEmpty(PrecificacaoContrato.Validar(Cfg(), [new(10, 10, 6, 0, true)]));
        Assert.NotEmpty(PrecificacaoContrato.Validar(Cfg(), [new(-1, null, 6, 0, true)]));
    }
}

public sealed class EnvioRegrasTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(10, 2560)]
    [InlineData(11, 3600)] // teto de 1 hora
    [InlineData(20, 3600)]
    public void Backoff_dobra_ate_o_teto(int tentativa, int segundos) => Assert.Equal(TimeSpan.FromSeconds(segundos), EnvioRegras.Atraso(tentativa));

    [Theory]
    [InlineData(400, true)]
    [InlineData(404, true)]
    [InlineData(409, true)]
    [InlineData(422, true)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(408, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    [InlineData(503, false)]
    public void Erros_permanentes_sao_4xx_menos_401_403_408_429(int status, bool permanente) => Assert.Equal(permanente, EnvioRegras.ErroPermanente(status));
}
