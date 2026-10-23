namespace Crivosistemas.Api.Common;

public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>URL pública do painel, usada nos links dos e-mails.</summary>
    public string PublicUrl { get; set; } = "http://localhost:5173";

    public string Link(string path) => (string.IsNullOrWhiteSpace(PublicUrl) ? "http://localhost:5173" : PublicUrl).TrimEnd('/') + "/" + path.TrimStart('/');
}
