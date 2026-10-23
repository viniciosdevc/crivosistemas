import { describe, expect, it } from 'vitest'
import { addTier, configToForm, diffPayload, fromRows, moveTier, parseConfigForm, removeTier, toRows, validateTiers } from '@/lib/pricing'
import type { ConfigCalculo, FaixaPix, PayloadPrecificacao } from '@/lib/types'

const faixas: FaixaPix[] = [
  { valorMinUsd: 10.01, valorMaxUsd: 15, multiplicador: 6, ordem: 1, ativo: true },
  { valorMinUsd: 0, valorMaxUsd: 10, multiplicador: 6.2, ordem: 0, ativo: true },
  { valorMinUsd: 15.01, valorMaxUsd: null, multiplicador: 5.9, ordem: 2, ativo: false },
]

describe('editor de faixas', () => {
  it('carrega ordenado por ordem, com vírgula decimal e sem limite vazio', () => {
    const rows = toRows(faixas)
    expect(rows.map((r) => r.min)).toEqual(['0', '10,01', '15,01'])
    expect(rows[1]).toMatchObject({ max: '15', mult: '6' })
    expect(rows[2]).toMatchObject({ max: '', ativo: false })
  })

  it('volta ao DTO numerando a ordem pela posição', () => {
    const out = fromRows(toRows(faixas))
    expect(out.map((f) => f.ordem)).toEqual([0, 1, 2])
    expect(out[0]).toEqual({ valorMinUsd: 0, valorMaxUsd: 10, multiplicador: 6.2, ordem: 0, ativo: true })
    expect(out[2]!.valorMaxUsd).toBeNull()
  })

  it('adiciona logo depois do teto da última faixa, copiando o multiplicador', () => {
    const rows = addTier(toRows(faixas.slice(0, 2)))
    expect(rows).toHaveLength(3)
    expect(rows[2]).toMatchObject({ min: '15,01', max: '', mult: '6', ativo: true })
    expect(addTier([])[0]).toMatchObject({ min: '0', ativo: true })
  })

  it('remove e reordena sem mexer na lista original', () => {
    const rows = toRows(faixas)
    expect(removeTier(rows, 1)).toHaveLength(2)
    expect(rows).toHaveLength(3)
    expect(moveTier(rows, 0, 1).map((r) => r.min)).toEqual(['10,01', '0', '15,01'])
    expect(moveTier(rows, 2, -1).map((r) => r.min)).toEqual(['0', '15,01', '10,01'])
    expect(moveTier(rows, 0, -1)).toBe(rows)
    expect(moveTier(rows, 2, 1)).toBe(rows)
  })

  it('valida como o servidor', () => {
    const rows = toRows(faixas)
    expect(validateTiers(rows).valid).toBe(true)
    const bad = rows.map((r) => ({ ...r }))
    bad[0]!.max = '0'
    bad[1]!.mult = '0'
    bad[2]!.min = ''
    const v = validateTiers(bad)
    expect(v.valid).toBe(false)
    expect(v.errors[0]!.max).toMatch(/maior que o mínimo/)
    expect(v.errors[1]!.mult).toBeTruthy()
    expect(v.errors[2]!.min).toBeTruthy()
  })

  it('avisa de sobreposição e de nenhuma faixa ativa', () => {
    const rows = toRows(faixas).map((r) => ({ ...r, ativo: true }))
    rows[1]!.min = '9'
    expect(validateTiers(rows).warnings.join(' ')).toMatch(/se sobrepõem/)
    const off = toRows(faixas).map((r) => ({ ...r, ativo: false }))
    expect(validateTiers(off).warnings.join(' ')).toMatch(/Nenhuma faixa ativa/)
  })
})

const config: ConfigCalculo = { valorKFeira: 13, valorKTrocas: 12, moedasPorK: 1000, dolarPadraoCompra: 5.7, dolarPadraoVenda: 6, taxaPrata: 3, margemVendaCambiosPercent: 20 }

describe('formulário de configuração', () => {
  it('vai e volta sem perder valores', () => {
    const form = configToForm(config)
    expect(form.dolarPadraoCompra).toBe('5,7')
    expect(parseConfigForm(form)).toEqual({ errors: {}, values: config })
  })

  it('rejeita vazio, negativo e zero onde não pode', () => {
    const form = { ...configToForm(config), taxaPrata: '', moedasPorK: '0', valorKFeira: '-1' }
    const { errors, values } = parseConfigForm(form)
    expect(values).toBeNull()
    expect(errors.taxaPrata).toBeTruthy()
    expect(errors.moedasPorK).toMatch(/maior que zero/)
    expect(errors.valorKFeira).toBeTruthy()
  })
})

describe('histórico', () => {
  const base: PayloadPrecificacao = {
    versao: 1,
    configuracao_calculo: { valor_k_feira: 13, valor_k_trocas: 12, moedas_por_k: 1000, dolar_padrao_compra: 5.7, dolar_padrao_venda: 6, taxa_prata: 3, margem_venda_cambios_percent: 20 },
    faixas_pix: [{ valor_min_usd: 0, valor_max_usd: 10, multiplicador: 6.2, ordem: 0, ativo: true }],
  }

  it('descreve o que mudou', () => {
    const next: PayloadPrecificacao = {
      ...base,
      versao: 2,
      configuracao_calculo: { ...base.configuracao_calculo, dolar_padrao_venda: 6.1 },
      faixas_pix: [...base.faixas_pix, { valor_min_usd: 10.01, valor_max_usd: null, multiplicador: 6, ordem: 1, ativo: true }],
    }
    expect(diffPayload(base, next)).toEqual(['Dólar padrão de venda: 6 → 6,1', 'Faixas PIX: 1 → 2'])
    expect(diffPayload(base, base)).toEqual([])
  })
})
