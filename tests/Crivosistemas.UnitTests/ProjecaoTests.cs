using Crivosistemas.Domain.Projecao;

namespace Crivosistemas.UnitTests;

public sealed class RegrasProjecaoTests
{
    [Fact]
    public void Media_diaria_conta_os_dias_sem_venda()
    {
        Assert.Equal(10m, RegrasProjecao.MediaDiaria(100m, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)));
        Assert.Equal(33.33m, RegrasProjecao.MediaDiaria(100m, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3)));
    }

    [Fact]
    public void Projetar_acumula_a_media_ajustada_pelo_crescimento_dia_a_dia()
    {
        var dias = RegrasProjecao.Projetar(100m, 10m, 50m, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 3));
        Assert.Equal(3, dias.Count);
        Assert.Equal(15m, dias[0].LucroAcumulado);
        Assert.Equal(45m, dias[2].LucroAcumulado);
        Assert.Equal(450m, dias[2].FaturamentoAcumulado);
        Assert.Equal(new DateOnly(2026, 2, 3), dias[2].Dia);
    }

    [Fact]
    public void Projetar_com_queda_de_100_por_cento_zera_tudo()
        => Assert.All(RegrasProjecao.Projetar(100m, 10m, -100m, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 5)), d => Assert.Equal(0m, d.LucroAcumulado));

    [Fact]
    public void Validar_exige_base_valida_e_data_final_depois_da_base()
    {
        var hoje = new DateOnly(2026, 3, 1);
        Assert.Empty(RegrasProjecao.Validar(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), new DateOnly(2026, 4, 1), hoje));
        Assert.Contains("baseDe", RegrasProjecao.Validar(new DateOnly(2026, 2, 10), new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 1), hoje).Keys);
        Assert.Contains("baseAte", RegrasProjecao.Validar(new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 5), new DateOnly(2026, 4, 1), hoje).Keys);
        Assert.Contains("dataFinal", RegrasProjecao.Validar(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), new DateOnly(2026, 2, 28), hoje).Keys);
        Assert.Contains("dataFinal", RegrasProjecao.Validar(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), new DateOnly(2030, 1, 1), hoje).Keys);
    }
}
