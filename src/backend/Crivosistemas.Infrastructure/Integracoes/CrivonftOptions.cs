namespace Crivosistemas.Infrastructure.Integracoes;

/// <summary>Configuração <c>Integrations:Crivonft</c>. Sem ela, os endpoints de integração respondem 503 e o worker não envia nem puxa nada.</summary>
public sealed class CrivonftOptions
{
    public const string Section = "Integrations:Crivonft";

    /// <summary>Endereço do crivonft, sem barra no fim.</summary>
    public string BaseUrl { get; set; } = "";
    /// <summary>Bearer dos dois sentidos (GESTAO_TOKEN no crivonft).</summary>
    public string Token { get; set; } = "";
    /// <summary>Segredo do HMAC dos dois sentidos (GESTAO_HMAC_SECRET no crivonft).</summary>
    public string HmacSecret { get; set; } = "";
    /// <summary>Bearer das rotas /api/export/* (IMPORT_SECRET do crivonft).</summary>
    public string ExportToken { get; set; } = "";

    /// <summary>Recebe eventos e envia regras de preço (precisa de token e segredo; a URL só é necessária para enviar).</summary>
    public bool RecebeEventos => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(HmacSecret);
    public bool Envia => RecebeEventos && !string.IsNullOrWhiteSpace(BaseUrl);
    public bool Exporta => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ExportToken);
}
