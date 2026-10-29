using System.Text.Json;
using System.Text.Json.Serialization;
using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Precificacao;

/// <summary>Configuração de cálculo (singleton). Este sistema é o dono; o crivonft guarda uma cópia.</summary>
public class PrecificacaoConfig : Entity
{
    public decimal ValorKFeira { get; set; }
    public decimal ValorKTrocas { get; set; }
    public decimal MoedasPorK { get; set; }
    public decimal DolarPadraoCompra { get; set; }
    public decimal DolarPadraoVenda { get; set; }
    public decimal TaxaPrata { get; set; }
    public decimal MargemVendaCambiosPercent { get; set; }
    /// <summary>Versão incremental do conjunto (configuração + faixas). Cada salvamento soma 1.</summary>
    public int Versao { get; set; }
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
    public Guid? AtualizadoPor { get; set; }
}

public class FaixaPix : Entity
{
    public decimal ValorMinUsd { get; set; }
    public decimal? ValorMaxUsd { get; set; }
    public decimal Multiplicador { get; set; }
    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;
}

public class PrecificacaoHistorico : Entity
{
    public int Versao { get; set; }
    public Guid? UserId { get; set; }
    public required string UserNome { get; set; }
    /// <summary>"painel" ou "importacao_crivonft".</summary>
    public required string Origem { get; set; }
    public required string Antes { get; set; }
    public required string Depois { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public enum EstadoEnvio { Pendente = 1, Enviado = 2, Falhou = 3, Substituido = 4 }

/// <summary>Fila de envio das regras ao crivonft (PUT /api/integracoes/gestao/precificacao). Gravada na mesma transação do salvamento.</summary>
public class PrecificacaoEnvio : Entity
{
    public int Versao { get; set; }
    /// <summary>Corpo exato do contrato (snake_case).</summary>
    public required string Payload { get; set; }
    public EstadoEnvio Estado { get; set; } = EstadoEnvio.Pendente;
    public int Tentativas { get; set; }
    public DateTime ProximaTentativaEm { get; set; } = DateTime.UtcNow;
    public DateTime? UltimaTentativaEm { get; set; }
    public DateTime? EnviadoEm { get; set; }
    public int? UltimoStatusHttp { get; set; }
    public string? UltimoErro { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Configuração de cálculo em memória (double, como no crivonft, para o resultado bater centavo a centavo).</summary>
public sealed record ConfigCalculo(double ValorKFeira, double ValorKTrocas, double MoedasPorK, double DolarPadraoCompra, double DolarPadraoVenda,
    double TaxaPrata, double MargemVendaCambiosPercent)
{
    /// <summary>Valores padrão de negócio do crivonft (src/lib/calculator.ts).</summary>
    public static readonly ConfigCalculo Padrao = new(13, 12, 1000, 5.7, 6, 3, 20);
}

public sealed record FaixaCalculo(double ValorMinUsd, double? ValorMaxUsd, double Multiplicador, int Ordem = 0, bool Ativo = true);

public sealed record CalculoPixResultado(double CustoUsd, double FaixaMultiplicador, double PrecoPelaFaixa, double PisoBrl, double PrecoPixBrl, bool AjustadoPeloPiso);

/// <summary>Cálculo do preço PIX, réplica fiel de <c>calculateFromUsd</c> do crivonft (src/lib/calculator.ts):
/// faixa pelo custo em USD, <c>usd × multiplicador</c>, e o piso (custo total com taxa de prata em BRL) nunca pode ser furado.</summary>
public static class CalculoPreco
{
    public static readonly IReadOnlyList<FaixaCalculo> FaixasPadrao =
    [
        new(0, 10, 6.2), new(10.01, 15, 6), new(15.01, 25, 6), new(25.01, null, 5.9),
    ];

    /// <summary>Arredondamento do crivonft: <c>Math.round(v * 100) / 100</c> (metade sobe), 0 para valores não finitos.</summary>
    public static double Arredondar(double v) => double.IsFinite(v) ? Math.Floor(v * 100 + 0.5) / 100 : 0;

    /// <summary>Faixa que contém o valor. Fora de qualquer faixa, vale a última por valor mínimo. Só entram faixas ativas;
    /// sem nenhuma ativa, usa as padrão (como o crivonft).</summary>
    public static FaixaCalculo EncontrarFaixa(double usd, IEnumerable<FaixaCalculo> faixas)
    {
        var ativas = faixas.Where(f => f.Ativo).ToList();
        if (ativas.Count == 0) ativas = FaixasPadrao.ToList();
        var ordenadas = ativas.OrderBy(f => f.ValorMinUsd).ThenBy(f => f.Ordem).ToList();
        return ordenadas.FirstOrDefault(f => usd >= f.ValorMinUsd && (f.ValorMaxUsd is null || usd <= f.ValorMaxUsd)) ?? ordenadas[^1];
    }

    public static CalculoPixResultado PrecoPix(double precoUsd, ConfigCalculo cfg, IEnumerable<FaixaCalculo> faixas)
    {
        var valorK = Math.Max(cfg.ValorKFeira, 0.0001);
        var precoBaseBrl = precoUsd * cfg.DolarPadraoVenda;
        var precoCambiosBase = precoBaseBrl * cfg.MoedasPorK / valorK;
        var custoTotalCambios = precoCambiosBase + cfg.TaxaPrata;
        var piso = custoTotalCambios * cfg.ValorKFeira / Math.Max(cfg.MoedasPorK, 0.0001);

        var faixa = EncontrarFaixa(precoUsd, faixas);
        var pelaFaixa = precoUsd * faixa.Multiplicador;
        var final = pelaFaixa; // PIX é sempre o preço da faixa; custo nunca altera o preço
        return new CalculoPixResultado(Arredondar(precoUsd), faixa.Multiplicador, Arredondar(pelaFaixa), Arredondar(piso), Arredondar(final), false);
    }
}

/// <summary>Corpo do contrato (seção 3) e sua validação, espelhando <c>validarPrecificacao</c> do crivonft para nunca enviar algo que ele recuse.</summary>
public static class PrecificacaoContrato
{
    private static readonly JsonSerializerOptions Opcoes = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public sealed record ConfigDto(
        [property: JsonPropertyName("valor_k_feira")] decimal ValorKFeira,
        [property: JsonPropertyName("valor_k_trocas")] decimal ValorKTrocas,
        [property: JsonPropertyName("moedas_por_k")] decimal MoedasPorK,
        [property: JsonPropertyName("dolar_padrao_compra")] decimal DolarPadraoCompra,
        [property: JsonPropertyName("dolar_padrao_venda")] decimal DolarPadraoVenda,
        [property: JsonPropertyName("taxa_prata")] decimal TaxaPrata,
        [property: JsonPropertyName("margem_venda_cambios_percent")] decimal MargemVendaCambiosPercent);

    public sealed record FaixaDto(
        [property: JsonPropertyName("valor_min_usd")] decimal ValorMinUsd,
        [property: JsonPropertyName("valor_max_usd")] decimal? ValorMaxUsd,
        [property: JsonPropertyName("multiplicador")] decimal Multiplicador,
        [property: JsonPropertyName("ordem")] int Ordem,
        [property: JsonPropertyName("ativo")] bool Ativo);

    public sealed record PayloadDto(
        [property: JsonPropertyName("versao")] int Versao,
        [property: JsonPropertyName("configuracao_calculo")] ConfigDto ConfiguracaoCalculo,
        [property: JsonPropertyName("faixas_pix")] IReadOnlyList<FaixaDto> FaixasPix);

    public static string Serializar(PayloadDto p) => JsonSerializer.Serialize(p, Opcoes);

    public static PayloadDto? Ler(string json) => JsonSerializer.Deserialize<PayloadDto>(json, Opcoes);

    /// <summary>Lista de problemas (vazia = válido). Mesmas regras do crivonft.</summary>
    public static List<string> Validar(ConfigDto c, IReadOnlyList<FaixaDto> faixas)
    {
        var erros = new List<string>();
        void NaoNeg(string nome, decimal v) { if (v < 0) erros.Add($"{nome} deve ser um número >= 0."); }
        NaoNeg("valorKFeira", c.ValorKFeira);
        NaoNeg("valorKTrocas", c.ValorKTrocas);
        NaoNeg("moedasPorK", c.MoedasPorK);
        NaoNeg("dolarPadraoCompra", c.DolarPadraoCompra);
        NaoNeg("dolarPadraoVenda", c.DolarPadraoVenda);
        NaoNeg("taxaPrata", c.TaxaPrata);
        NaoNeg("margemVendaCambiosPercent", c.MargemVendaCambiosPercent);
        if (c.MoedasPorK <= 0) erros.Add("moedasPorK deve ser > 0.");
        if (c.ValorKFeira <= 0) erros.Add("valorKFeira deve ser > 0.");
        if (faixas.Count > 200) erros.Add("Faixas demais (máx. 200).");
        for (var i = 0; i < faixas.Count; i++)
        {
            var f = faixas[i];
            if (f.ValorMinUsd < 0) erros.Add($"faixasPix[{i}].valorMinUsd inválido.");
            if (f.ValorMaxUsd is { } max && max <= f.ValorMinUsd) erros.Add($"faixasPix[{i}].valorMaxUsd deve ser maior que o mínimo.");
            if (f.Multiplicador <= 0) erros.Add($"faixasPix[{i}].multiplicador deve ser > 0.");
        }
        return erros;
    }
}

/// <summary>Regras de reenvio das regras de preço ao crivonft (as mesmas do outbox do crivonft).</summary>
public static class EnvioRegras
{
    public const int MaxTentativas = 20;

    /// <summary>Espera antes da próxima tentativa depois da <paramref name="tentativa"/>-ésima falha: 5 s dobrando até 1 h.</summary>
    public static TimeSpan Atraso(int tentativa)
    {
        var segundos = 5d * Math.Pow(2, Math.Max(0, tentativa - 1));
        return TimeSpan.FromSeconds(Math.Min(segundos, 3600));
    }

    /// <summary>Resposta 4xx que o crivonft considera erro permanente: tudo menos 401/403/408/429, que repetem.</summary>
    public static bool ErroPermanente(int statusHttp) => statusHttp is >= 400 and < 500 and not (401 or 403 or 408 or 429);
}
