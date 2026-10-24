using Crivosistemas.Domain.Posicao;

namespace Crivosistemas.UnitTests;

public sealed class RegrasPosicaoTests
{
    [Fact]
    public void Diferenca_e_informado_menos_sistema_e_nula_sem_conferencia()
    {
        Assert.Equal(-50m, RegrasPosicao.Diferenca(950m, 1000m));
        Assert.Equal(0m, RegrasPosicao.Diferenca(10m, 10m));
        Assert.Null(RegrasPosicao.Diferenca(null, 1000m));
    }

    [Fact]
    public void Total_operacional_soma_caixa_e_dolar_ao_custo_medio_usando_o_conferido()
    {
        Assert.Equal(1500m, RegrasPosicao.TotalOperacionalBrl(1000m, 900m, 100m, 80m, 5m));
        Assert.Equal(1300m, RegrasPosicao.TotalOperacionalBrl(null, 900m, null, 80m, 5m));
    }
}
