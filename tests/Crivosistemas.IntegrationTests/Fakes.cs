using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Crivosistemas.Infrastructure.Email;

namespace Crivosistemas.IntegrationTests;

public sealed partial class CapturingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Espera o dispatcher em segundo plano e devolve o token do último link enviado a um endereço.</summary>
    public async Task<string> TokenFor(string to, string path)
    {
        for (var i = 0; i < 50; i++)
        {
            var match = Sent.Reverse().Where(m => m.To == to).Select(m => TokenRegex().Match(m.Text))
                .FirstOrDefault(m => m.Success && m.Value.Contains(path));
            if (match is not null) return match.Groups[1].Value;
            await Task.Delay(100);
        }
        throw new InvalidOperationException($"Nenhum e-mail com {path} enviado para {to}");
    }

    [GeneratedRegex(@"https?://\S+\?token=([A-Za-z0-9_-]+)")]
    private static partial Regex TokenRegex();
}

/// <summary>Cliente do crivonft falso: serve páginas de exportação preparadas e guarda os PUTs de precificação recebidos.</summary>
public sealed class FakeCrivonftClient : Crivosistemas.Infrastructure.Integracoes.ICrivonftClient
{
    public bool EnvioConfigurado { get; set; } = true;
    public bool ExportConfigurado { get; set; } = true;
    public List<System.Text.Json.JsonElement> Vendas { get; } = [];
    public List<System.Text.Json.JsonElement> Clientes { get; } = [];
    public Crivosistemas.Domain.Precificacao.PrecificacaoContrato.PayloadDto? Remota { get; set; }
    public List<string> Enviados { get; } = [];
    /// <summary>Simula a rota /reenviar: false = 404 (crivonft desatualizado). Outro status de erro em <see cref="StatusDoReenvio"/>.</summary>
    public bool RotaReenviarExiste { get; set; }
    public int StatusDoReenvio { get; set; } = 200;
    /// <summary>Janelas acima deste número de dias voltam truncadas (para testar a divisão).</summary>
    public int TruncarAcimaDeDias { get; set; } = int.MaxValue;
    public List<(DateOnly De, DateOnly Ate)> Reenvios { get; } = [];
    /// <summary>Status HTTP que o PUT devolve; 0 simula falha de rede.</summary>
    public int StatusDoPut { get; set; } = 200;
    public List<(DateOnly? De, DateOnly? Ate, int Offset)> ConsultasVendas { get; } = [];

    public static System.Text.Json.JsonElement Linha(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    public Task<Crivosistemas.Infrastructure.Integracoes.ExportPagina> ExportarVendasAsync(DateOnly? de, DateOnly? ate, int limit, int offset, CancellationToken ct)
    {
        ConsultasVendas.Add((de, ate, offset));
        return Task.FromResult(new Crivosistemas.Infrastructure.Integracoes.ExportPagina(Vendas.Count, limit, offset, Vendas.Skip(offset).Take(limit).ToList()));
    }

    public Task<Crivosistemas.Infrastructure.Integracoes.ExportPagina> ExportarClientesAsync(int limit, int offset, CancellationToken ct) =>
        Task.FromResult(new Crivosistemas.Infrastructure.Integracoes.ExportPagina(Clientes.Count, limit, offset, Clientes.Skip(offset).Take(limit).ToList()));

    public Dictionary<string, string> Imagens { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int ConsultasImagens { get; set; }

    public Task<IReadOnlyDictionary<string, string?>> ImagensDeItensAsync(IReadOnlyList<string> nomes, CancellationToken ct)
    {
        ConsultasImagens++;
        return Task.FromResult<IReadOnlyDictionary<string, string?>>(nomes.Distinct().ToDictionary(n => n, n => Imagens.TryGetValue(n, out var u) ? u : null));
    }

    public List<System.Text.Json.JsonElement> EstoqueUsd { get; } = [];
    public List<System.Text.Json.JsonElement> EstoqueEsmeralda { get; } = [];

    public Task<Crivosistemas.Infrastructure.Integracoes.ExportPagina> ExportarEstoqueAsync(string tipo, int limit, int offset, CancellationToken ct)
    {
        var l = tipo == "usd" ? EstoqueUsd : EstoqueEsmeralda;
        return Task.FromResult(new Crivosistemas.Infrastructure.Integracoes.ExportPagina(l.Count, limit, offset, l.Skip(offset).Take(limit).ToList()));
    }

    public Task<Crivosistemas.Infrastructure.Integracoes.ReenvioResposta> ReenviarPeriodoAsync(DateOnly de, DateOnly ate, CancellationToken ct)
    {
        if (!RotaReenviarExiste) throw new Crivosistemas.Infrastructure.Integracoes.CrivonftException("404", 404);
        if (StatusDoReenvio != 200) throw new Crivosistemas.Infrastructure.Integracoes.CrivonftException("erro", StatusDoReenvio == 0 ? null : StatusDoReenvio);
        Reenvios.Add((de, ate));
        return Task.FromResult(new Crivosistemas.Infrastructure.Integracoes.ReenvioResposta(2, 1, 1, ate.DayNumber - de.DayNumber + 1 > TruncarAcimaDeDias));
    }

    public Task<Crivosistemas.Domain.Precificacao.PrecificacaoContrato.PayloadDto> LerPrecificacaoAsync(CancellationToken ct) =>
        Task.FromResult(Remota ?? throw new Crivosistemas.Infrastructure.Integracoes.CrivonftException("sem estado remoto", 404));

    public List<string?> Convites { get; } = [];

    public Task<string> ConvitesAsync(string? corpoJson, CancellationToken ct)
    {
        Convites.Add(corpoJson);
        return Task.FromResult(corpoJson is null ? "{\"links\":[],\"nicks\":[]}" : "{\"ok\":true,\"url\":\"https://loja.test/convite/abc\"}");
    }

    public Task<Crivosistemas.Infrastructure.Integracoes.EnvioResposta> EnviarPrecificacaoAsync(string corpoJson, CancellationToken ct)
    {
        Enviados.Add(corpoJson);
        if (StatusDoPut == 0) throw new Crivosistemas.Infrastructure.Integracoes.CrivonftException("rede fora");
        return Task.FromResult(new Crivosistemas.Infrastructure.Integracoes.EnvioResposta(StatusDoPut, StatusDoPut is >= 200 and < 300, null, null));
    }

    public List<(HttpMethod Metodo, string Caminho, string? Corpo)> ChamadasGestao { get; } = [];
    public Crivosistemas.Infrastructure.Integracoes.GestaoResposta RespostaGestao { get; set; } = new(200, "{}");

    public Task<Crivosistemas.Infrastructure.Integracoes.GestaoResposta> ChamarGestaoAsync(HttpMethod metodo, string caminho, object? corpo, CancellationToken ct)
    {
        ChamadasGestao.Add((metodo, caminho, corpo is null ? null : System.Text.Json.JsonSerializer.Serialize(corpo, System.Text.Json.JsonSerializerOptions.Web)));
        return Task.FromResult(RespostaGestao);
    }
}
