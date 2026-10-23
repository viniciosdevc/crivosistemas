import { describe, expect, it } from 'vitest'
import { calculateFromUsd, calculateSale, esmeraldasParaUsd, findPixRange } from '@/lib/calculator'
import type { ConfigCalculo, FaixaPix } from '@/lib/types'

const config: ConfigCalculo = { valorKFeira: 13, valorKTrocas: 12, moedasPorK: 1000, dolarPadraoCompra: 5.7, dolarPadraoVenda: 6, taxaPrata: 3, margemVendaCambiosPercent: 20 }
const faixas: FaixaPix[] = [
  { valorMinUsd: 0, valorMaxUsd: 10, multiplicador: 6.2, ordem: 0, ativo: true },
  { valorMinUsd: 10.01, valorMaxUsd: 15, multiplicador: 6, ordem: 1, ativo: true },
  { valorMinUsd: 15.01, valorMaxUsd: 25, multiplicador: 6, ordem: 2, ativo: true },
  { valorMinUsd: 25.01, valorMaxUsd: null, multiplicador: 5.9, ordem: 3, ativo: true },
]

describe('calculateFromUsd', () => {
  it('US$ 10: custo com dólar de compra, preço com dólar de venda', () => {
    const r = calculateFromUsd(10, config, faixas)
    // custo: 10 × 5,7 = 57 BRL → 57 × 1000 / 13 = 4384,6 C (+3 de prata)
    expect(r.custoBrl).toBe(57)
    expect(r.custoCambiosBase).toBe(4385)
    expect(r.custoCambios).toBe(4388)
    // venda: 10 × 6 = 60 → 4615,38 C; + 3 = 4618,38; × 1,2 = 5542,06 → teto 5543
    expect(r.precoBaseBrl).toBe(60)
    expect(r.precoCambiosBase).toBe(4615)
    expect(r.precoVendaCambios).toBe(5543)
    // faixa 0–10 → × 6,2 = 62; piso = 4618,38 × 13 / 1000 = 60,04
    expect(r.pixMultiplicador).toBe(6.2)
    expect(r.precoVendaPix).toBe(62)
    expect(r.pixAjustadoPeloPiso).toBe(false)
  })

  it('US$ 30: última faixa, sem teto', () => {
    const r = calculateFromUsd(30, config, faixas)
    expect(r.custoBrl).toBe(171)
    expect(r.pixMultiplicador).toBe(5.9)
    // faixa daria 30 × 5,9 = 177, abaixo do piso (13849,15 × 13 / 1000 = 180,04)
    expect(r.precoVendaPixFaixa).toBe(177)
    expect(r.precoVendaPix).toBe(180.04)
    expect(r.pixAjustadoPeloPiso).toBe(true)
    // 180 × 1000 / 13 = 13846,15 + 3 = 13849,15 × 1,2 = 16618,98 → 16619
    expect(r.precoVendaCambios).toBe(16619)
  })

  it('o piso prevalece quando a faixa daria menos que o custo total', () => {
    const baixa: FaixaPix[] = [{ valorMinUsd: 0, valorMaxUsd: null, multiplicador: 1, ordem: 0, ativo: true }]
    const r = calculateFromUsd(10, config, baixa)
    expect(r.precoVendaPixFaixa).toBe(10)
    expect(r.precoVendaPix).toBe(60.04)
    expect(r.pixAjustadoPeloPiso).toBe(true)
  })

  it('faixas inativas são ignoradas; sem nenhuma ativa valem as padrão', () => {
    const mix = faixas.map((f, i) => (i === 0 ? { ...f, ativo: false, multiplicador: 99 } : f))
    expect(findPixRange(12, mix).multiplicador).toBe(6)
    expect(findPixRange(5, mix).multiplicador).toBe(5.9) // sem faixa que cubra: cai na última ativa
    const nenhuma = faixas.map((f) => ({ ...f, ativo: false, multiplicador: 99 }))
    expect(findPixRange(5, nenhuma).multiplicador).toBe(6.2)
    expect(findPixRange(30, nenhuma).multiplicador).toBe(5.9)
  })
})

describe('esmeraldas', () => {
  it('300 esmeraldas com K a US$ 9,44 custam US$ 2,832', () => {
    expect(esmeraldasParaUsd(300, 9.44)).toBeCloseTo(2.832, 6)
  })
})

describe('calculateSale', () => {
  it('lucro e margem de uma venda só em PIX', () => {
    const s = calculateSale({ custoUsd: 10, valorVendaPix: 62, valorVendaCambios: 0, settings: config, ranges: faixas })
    // custo: 4388 C × 13 / 1000 = 57,044 BRL → lucro 62 − 57,044 = 4,956
    expect(s.custoBrl).toBe(57.04)
    expect(s.lucroBrl).toBe(4.96)
    expect(s.margemPercent).toBe(8.69)
    expect(s.taxaPrataAplicada).toBe(3)
  })
})
