using Crivosistemas.Domain.Posicao;

namespace Crivosistemas.UnitTests;

public sealed class RegrasPosicaoTests
{
    [Fact]
    public void Total_soma_conta_dolar_ao_preco_de_venda_cambios_ao_valor_do_k_e_esmeraldas()
    {
        // 1000 na conta + 100 US$ × 5,5 + 20000 câmbios × 0,01 + 50 esmeraldas × 2
        Assert.Equal(1000m + 550m + 200m + 100m, RegrasPosicao.TotalBrl(1000m, 100m, 20000m, 50m, 5.5m, 0.01m, 2m));
        Assert.Equal(0m, RegrasPosicao.TotalBrl(0, 0, 0, 0, 5.5m, 0.01m, 2m));
    }
}
