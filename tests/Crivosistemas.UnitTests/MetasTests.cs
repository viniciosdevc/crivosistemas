using Crivosistemas.Domain.Metas;

namespace Crivosistemas.UnitTests;

public sealed class RegrasMetasTests
{
    [Fact]
    public void Segunda_e_dia_da_semana_vao_de_segunda_a_domingo()
    {
        var domingo = new DateOnly(2026, 10, 25);
        Assert.Equal(new DateOnly(2026, 10, 19), RegrasMetas.Segunda(domingo));
        Assert.Equal(7, RegrasMetas.DiaDaSemana(domingo));
        Assert.Equal(new DateOnly(2026, 10, 26), RegrasMetas.Segunda(new DateOnly(2026, 10, 26)));
        Assert.Equal(1, RegrasMetas.DiaDaSemana(new DateOnly(2026, 10, 26)));
        Assert.Equal(new DateOnly(2026, 10, 26), RegrasMetas.Segunda(new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public void Resumo_em_andamento_calcula_falta_ritmo_e_necessario_por_dia_util()
    {
        var r = RegrasMetas.Resumir(1000m, 300m, 3); // quarta-feira
        Assert.Equal(StatusMeta.EmAndamento, r.Status);
        Assert.Equal(700m, r.FaltaBrl);
        Assert.Equal(30m, r.ProgressoPercent);
        Assert.Equal(42.86m, r.RitmoIdealPercent);
        Assert.Equal(100m, r.MediaDiariaBrl);
        Assert.Equal(700m, r.ProjecaoFechamentoBrl);
        Assert.Equal(4, r.DiasUteisRestantes); // quarta, quinta, sexta e sábado
        Assert.Equal(175m, r.NecessarioPorDiaUtilBrl);
    }

    [Fact]
    public void Resumo_meta_batida_limita_o_progresso_a_100()
    {
        var r = RegrasMetas.Resumir(1000m, 1500m, 5);
        Assert.Equal(StatusMeta.Batida, r.Status);
        Assert.Equal(0m, r.FaltaBrl);
        Assert.Equal(100m, r.ProgressoPercent);
    }

    [Fact]
    public void Resumo_no_domingo_sem_bater_a_meta_e_ultimo_dia()
    {
        var r = RegrasMetas.Resumir(1000m, 400m, 7);
        Assert.Equal(StatusMeta.UltimoDia, r.Status);
        Assert.Equal(0, r.DiasUteisRestantes);
        Assert.Equal(0m, r.NecessarioPorDiaUtilBrl);
    }

    [Fact]
    public void Validar_exige_segunda_e_valor_positivo()
    {
        Assert.Null(RegrasMetas.Validar(new DateOnly(2026, 10, 26), 500m));
        Assert.NotNull(RegrasMetas.Validar(new DateOnly(2026, 10, 27), 500m));
        Assert.NotNull(RegrasMetas.Validar(new DateOnly(2026, 10, 26), 0m));
        Assert.NotNull(RegrasMetas.Validar(new DateOnly(2026, 10, 26), -5m));
    }
}
