using Crivosistemas.Domain.Common;

namespace Crivosistemas.Domain.Integracoes;

public enum EstadoEvento
{
    /// <summary>Gravado, ainda não processado (ou processamento interrompido).</summary>
    Recebido = 1,
    Processado = 2,
    /// <summary>Erro inesperado ao processar: o crivonft recebeu 5xx e vai repetir; também pode ser reprocessado pelo admin.</summary>
    Falhou = 3,
    /// <summary>Payload inválido: erro permanente (4xx).</summary>
    Rejeitado = 4,
}

/// <summary>Evento bruto vindo do crivonft, gravado antes de qualquer processamento. O id do crivonft é único: repetição vira "duplicado".</summary>
public class EventoRecebido : Entity
{
    public Guid EventoId { get; set; }
    public required string Tipo { get; set; }
    public DateTime OcorridoEm { get; set; }
    public DateTime RecebidoEm { get; set; } = DateTime.UtcNow;
    public EstadoEvento Estado { get; set; } = EstadoEvento.Recebido;
    public required string Corpo { get; set; }
    public string? Erro { get; set; }
    public int Tentativas { get; set; }
    public DateTime? ProcessadoEm { get; set; }
    /// <summary>Resultado do último processamento (ex.: "aplicado", "ignorado: mais antigo que o gravado").</summary>
    public string? Resultado { get; set; }
    /// <summary>Chaves extraídas do payload para consulta (vendas e clientes).</summary>
    public string? Origem { get; set; }
    public string? OrigemId { get; set; }
    public string? ClienteChave { get; set; }
}

public enum TipoReconciliacao { Noturna = 1, Manual = 2, CargaInicial = 3 }
public enum EstadoReconciliacao { Pendente = 1, Executando = 2, Concluida = 3, Falhou = 4 }

/// <summary>Uma execução de reconciliação (puxar vendas e clientes do crivonft). As manuais ficam "pendentes" até o Worker pegar.</summary>
public class ReconciliacaoExecucao : Entity
{
    public TipoReconciliacao Tipo { get; set; }
    public DateOnly? De { get; set; }
    public DateOnly? Ate { get; set; }
    public EstadoReconciliacao Estado { get; set; } = EstadoReconciliacao.Pendente;
    public Guid? SolicitadoPor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? IniciadoEm { get; set; }
    public DateTime? ConcluidoEm { get; set; }
    public int VendasLidas { get; set; }
    public int VendasAplicadas { get; set; }
    public int ClientesLidos { get; set; }
    public int ClientesCriados { get; set; }
    public string? Erro { get; set; }
    /// <summary>Observação que não é falha, ex.: "crivonft desatualizado: só vendas NFT" (execução parcial).</summary>
    public string? Aviso { get; set; }
}
