namespace Crivosistemas.Domain.Vendas;

/// <summary>Dias de calendário em America/Sao_Paulo. O Brasil não tem horário de verão desde 2019, então o fuso é UTC-3 fixo;
/// usa o fuso do sistema quando existe e cai para o deslocamento fixo quando a imagem não traz tzdata.</summary>
public static class FusoSp
{
    public const string Id = "America/Sao_Paulo";
    private static readonly TimeSpan Fixo = TimeSpan.FromHours(-3);

    private static readonly TimeZoneInfo? Zona = Tentar();

    private static TimeZoneInfo? Tentar()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(Id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }

    public static DateTime ParaLocal(DateTime utc)
    {
        var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return Zona is null ? u.Add(Fixo) : TimeZoneInfo.ConvertTimeFromUtc(u, Zona);
    }

    public static DateOnly Dia(DateTime utc) => DateOnly.FromDateTime(ParaLocal(utc));

    public static DateOnly Hoje(DateTime? agoraUtc = null) => Dia(agoraUtc ?? DateTime.UtcNow);

    /// <summary>Início (UTC) do dia de São Paulo.</summary>
    public static DateTime InicioDoDiaUtc(DateOnly dia)
    {
        var local = dia.ToDateTime(TimeOnly.MinValue);
        return Zona is null
            ? DateTime.SpecifyKind(local - Fixo, DateTimeKind.Utc)
            : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zona);
    }

    /// <summary>Intervalo [inicio, fim) em UTC que cobre os dias de SP de <paramref name="de"/> a <paramref name="ate"/>, inclusivos.</summary>
    public static (DateTime Inicio, DateTime Fim) Intervalo(DateOnly de, DateOnly ate) =>
        (InicioDoDiaUtc(de), InicioDoDiaUtc(ate.AddDays(1)));
}
