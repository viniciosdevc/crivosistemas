import { describe, expect, it } from 'vitest'
import { calculateSale } from '@/lib/calculator'
import { calcularVendaManual, montarVendaManual, vendaManualValida, zonedToIso, type EntradaVendaManual } from '@/lib/sale'
import type { RegrasPrecificacao } from '@/lib/types'

const regras: RegrasPrecificacao = {
  versao: 1,
  configuracao: { valorKFeira: 13, valorKTrocas: 12, moedasPorK: 1000, dolarPadraoCompra: 5.7, dolarPadraoVenda: 6, taxaPrata: 3, margemVendaCambiosPercent: 20 },
  faixasPix: [
    { valorMinUsd: 0, valorMaxUsd: 10, multiplicador: 6.2, ordem: 0, ativo: true },
    { valorMinUsd: 10.01, valorMaxUsd: null, multiplicador: 6, ordem: 1, ativo: true },
  ],
}
const custos = { usdCustoMedio: 5.2, esmeraldasCustoMedio: 0.01 }
const base: EntradaVendaManual = { forma: 'pix', origem: 'estoque_usd', custoUsd: '10', custoCambios: '', quantidadeEsmeraldas: '', usaTaxaPrata: false, valorPix: '70', valorCambios: '' }

describe('calcularVendaManual', () => {
  it('estoque em USD usa o custo médio do estoque como dólar de compra', () => {
    const r = calcularVendaManual(base, regras, custos)
    expect(r.dolarCompra).toBe(5.2)
    expect(r.custoUsd).toBe(10)
    expect(r.result.custoBrl).toBe(52)
    expect(r.result.lucroBrl).toBe(18)
  })

  it('sem custo médio no estoque, cai no dólar padrão das regras', () => {
    const r = calcularVendaManual(base, regras, { ...custos, usdCustoMedio: 0 })
    expect(r.dolarCompra).toBe(5.7)
    expect(r.result.custoBrl).toBeCloseTo(57, 1)
  })

  it('custo manual usa o dólar padrão, nunca o do estoque', () => {
    const r = calcularVendaManual({ ...base, origem: 'manual' }, regras, custos)
    expect(r.dolarCompra).toBe(5.7)
  })

  it('esmeraldas: custo USD = quantidade × custo médio das esmeraldas', () => {
    const r = calcularVendaManual({ ...base, origem: 'estoque_esmeralda', custoUsd: '999', quantidadeEsmeraldas: '500' }, regras, custos)
    expect(r.custoUsd).toBeCloseTo(5, 6)
    expect(r.qtdEsmeraldas).toBe(500)
  })

  it('taxa de prata só entra no custo quando marcada', () => {
    const sem = calcularVendaManual(base, regras, custos)
    const com = calcularVendaManual({ ...base, usaTaxaPrata: true }, regras, custos)
    expect(sem.result.taxaPrataAplicada).toBe(0)
    expect(com.result.taxaPrataAplicada).toBe(3)
    expect(com.result.custoBrl).toBeGreaterThan(sem.result.custoBrl)
  })

  it('comprado em câmbios usa o custo em câmbios e ignora o custo USD', () => {
    const r = calcularVendaManual({ ...base, forma: 'cambios', origem: 'cambios', custoUsd: '50', custoCambios: '4000', valorCambios: '5000' }, regras, custos)
    expect(r.custoUsd).toBe(0)
    expect(r.result.custoCambios).toBe(4000)
    expect(r.margemIdeal).toBe(20)
    expect(r.result.receitaTotalBrl).toBe(calculateSale({ custoUsd: 0, valorVendaPix: 0, valorVendaCambios: 5000, settings: regras.configuracao, ranges: regras.faixasPix, custoCambiosManual: 4000 }).receitaTotalBrl)
  })

  it('o valor da forma que não foi usada é ignorado', () => {
    const r = calcularVendaManual({ ...base, forma: 'pix', valorCambios: '9999' }, regras, custos)
    expect(r.valorCambios).toBe(0)
    expect(r.valorPix).toBe(70)
  })

  it('aceita vírgula decimal e marca margem abaixo do ideal', () => {
    const r = calcularVendaManual({ ...base, valorPix: '52,50' }, regras, custos)
    expect(r.valorPix).toBe(52.5)
    expect(r.abaixoDoIdeal).toBe(true)
  })
})

describe('vendaManualValida', () => {
  it('exige item e algum valor recebido', () => {
    expect(vendaManualValida(base, 'Capa')).toBe(true)
    expect(vendaManualValida(base, '  ')).toBe(false)
    expect(vendaManualValida({ ...base, valorPix: '' }, 'Capa')).toBe(false)
  })

  it('esmeraldas e câmbios exigem a quantidade e o custo', () => {
    expect(vendaManualValida({ ...base, origem: 'estoque_esmeralda' }, 'Capa')).toBe(false)
    expect(vendaManualValida({ ...base, origem: 'estoque_esmeralda', quantidadeEsmeraldas: '10' }, 'Capa')).toBe(true)
    expect(vendaManualValida({ ...base, origem: 'cambios' }, 'Capa')).toBe(false)
  })
})

describe('montarVendaManual', () => {
  it('zera o que não se aplica à origem e à forma de pagamento', () => {
    const body = montarVendaManual({ ...base, origem: 'estoque_esmeralda', custoUsd: '12', quantidadeEsmeraldas: '300', forma: 'cambios', valorPix: '5', valorCambios: '1234,6' }, {
      dataHora: '2026-10-22T15:00:00.000Z', nickname: ' ana ', nomeItem: ' Capa ', observacao: '', entregue: true,
    })
    expect(body).toMatchObject({ custoUsd: 0, custoCambios: 0, quantidadeEsmeraldas: 300, valorVendaPix: 0, valorVendaCambios: 1235, nicknameComprador: 'ana', nomeItem: 'Capa', entregue: true })
  })
})

describe('zonedToIso', () => {
  it('converte data e hora de São Paulo (UTC-3) para UTC', () => {
    expect(zonedToIso('2026-10-22', '14:30', 'America/Sao_Paulo')).toBe('2026-10-22T17:30:00.000Z')
  })

  it('vira o dia quando a hora local passa da meia-noite em UTC', () => {
    expect(zonedToIso('2026-10-22', '22:00', 'America/Sao_Paulo')).toBe('2026-10-23T01:00:00.000Z')
  })

  it('funciona em outro fuso', () => {
    expect(zonedToIso('2026-01-10', '08:00', 'UTC')).toBe('2026-01-10T08:00:00.000Z')
  })
})

describe('taxa da feira', () => {
  it('converte a taxa de câmbios para R$ pelo K da feira', async () => {
    const { taxaFeiraBrl, taxaFeiraPadrao } = await import('@/lib/sale')
    expect(taxaFeiraBrl({ taxaPrata: 500, valorKFeira: 13, moedasPorK: 1000 })).toBeCloseTo(6.5)
    expect(taxaFeiraPadrao('pix')).toBe(false)
    expect(taxaFeiraPadrao('cambios')).toBe(true)
    expect(taxaFeiraPadrao('hibrido')).toBe(true)
  })
})
