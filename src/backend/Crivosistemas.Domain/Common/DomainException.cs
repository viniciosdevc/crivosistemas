namespace Crivosistemas.Domain.Common;

/// <summary>Uma regra de negócio foi violada. A API mapeia para HTTP 422 (ou 409 em conflitos).</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ConflictException(string code, string message) : DomainException(code, message);
