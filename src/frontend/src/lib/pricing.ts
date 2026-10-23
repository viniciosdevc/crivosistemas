import { formatDecimal, parseDecimal } from './format'
import type { ConfigCalculo, FaixaPix, PayloadPrecificacao } from './types'

/** Linha do editor de faixas: campos como texto, para aceitar vírgula e campos vazios enquanto a pessoa digita. */
export interface TierRow {
  key: number
  min: string
  /** Vazio = sem limite superior. */
  max: string
  mult: string
  ativo: boolean
}

export interface TierErrors {
  min?: string
  max?: string
  mult?: string
}

let seq = 0
const num = (n: number) => String(n).replace('.', ',')

export function toRows(faixas: FaixaPix[]): TierRow[] {
  return [...faixas]
    .sort((a, b) => a.ordem - b.ordem)
    .map((f) => ({ key: ++seq, min: num(f.valorMinUsd), max: f.valorMaxUsd == null ? '' : num(f.valorMaxUsd), mult: num(f.multiplicador), ativo: f.ativo }))
}

/** Converte as linhas para o DTO. A ordem é a posição na lista. Linhas inválidas viram NaN: use `validateTiers` antes. */
export function fromRows(rows: TierRow[]): FaixaPix[] {
  return rows.map((r, i) => ({
    valorMinUsd: parseDecimal(r.min) ?? Number.NaN,
    valorMaxUsd: r.max.trim() === '' ? null : (parseDecimal(r.max) ?? Number.NaN),
    multiplicador: parseDecimal(r.mult) ?? Number.NaN,
    ordem: i,
    ativo: r.ativo,
  }))
}

/** Nova faixa começando logo depois do teto da última (ou do zero). */
export function addTier(rows: TierRow[]): TierRow[] {
  const last = rows[rows.length - 1]
  const lastMax = last ? parseDecimal(last.max) : null
  const lastMin = last ? parseDecimal(last.min) : null
  const min = lastMax != null ? Math.round((lastMax + 0.01) * 100) / 100 : lastMin != null ? Math.round((lastMin + 10) * 100) / 100 : 0
  return [...rows, { key: ++seq, min: num(min), max: '', mult: last?.mult ?? '', ativo: true }]
}

export function removeTier(rows: TierRow[], index: number): TierRow[] {
  return rows.filter((_, i) => i !== index)
}

/** Troca a linha de lugar com a vizinha; fora do alcance devolve a lista como está. */
export function moveTier(rows: TierRow[], index: number, delta: -1 | 1): TierRow[] {
  const to = index + delta
  if (to < 0 || to >= rows.length) return rows
  const next = [...rows]
  const [item] = next.splice(index, 1)
  next.splice(to, 0, item!)
  return next
}

/** Erros por linha (mesmas regras do servidor) e avisos de sobreposição entre faixas ativas. */
export function validateTiers(rows: TierRow[]) {
  const errors: TierErrors[] = rows.map(() => ({}))
  rows.forEach((r, i) => {
    const min = parseDecimal(r.min)
    const max = r.max.trim() === '' ? null : parseDecimal(r.max)
    const mult = parseDecimal(r.mult)
    if (min == null || min < 0) errors[i]!.min = 'Informe um valor maior ou igual a zero.'
    if (r.max.trim() !== '' && max == null) errors[i]!.max = 'Número inválido.'
    else if (max != null && min != null && max <= min) errors[i]!.max = 'Precisa ser maior que o mínimo.'
    if (mult == null || mult <= 0) errors[i]!.mult = 'Informe um valor maior que zero.'
  })
  const warnings: string[] = []
  const active = rows
    .map((r, i) => ({ i, min: parseDecimal(r.min), max: r.max.trim() === '' ? Infinity : parseDecimal(r.max), on: r.ativo }))
    .filter((r) => r.on && r.min != null && r.max != null)
    .sort((a, b) => a.min! - b.min!)
  for (let k = 1; k < active.length; k++) {
    if (active[k]!.min! <= active[k - 1]!.max!) warnings.push(`As faixas ${active[k - 1]!.i + 1} e ${active[k]!.i + 1} se sobrepõem.`)
  }
  if (rows.length > 0 && !rows.some((r) => r.ativo)) warnings.push('Nenhuma faixa ativa: o cálculo usará as faixas padrão do crivonft.')
  const valid = errors.every((e) => !e.min && !e.max && !e.mult)
  return { errors, warnings, valid }
}

export const configFields: { key: keyof ConfigCalculo; label: string; hint?: string; prefix?: string; suffix?: string }[] = [
  { key: 'valorKFeira', label: 'Valor do K na feira', hint: 'Reais por 1K de moeda vendido na feira.', prefix: 'R$' },
  { key: 'valorKTrocas', label: 'Valor do K em trocas', hint: 'Reais por 1K de moeda nas trocas.', prefix: 'R$' },
  { key: 'moedasPorK', label: 'Moedas por K', hint: 'Quantas moedas formam 1K.' },
  { key: 'dolarPadraoCompra', label: 'Dólar padrão de compra', prefix: 'R$' },
  { key: 'dolarPadraoVenda', label: 'Dólar padrão de venda', hint: 'Base do piso de preço.', prefix: 'R$' },
  { key: 'taxaPrata', label: 'Taxa de prata', hint: 'Somada ao custo em câmbios para formar o piso.' },
  { key: 'margemVendaCambiosPercent', label: 'Margem de venda em câmbios', suffix: '%' },
]

export type ConfigForm = Record<keyof ConfigCalculo, string>

export function configToForm(c: ConfigCalculo): ConfigForm {
  return Object.fromEntries(configFields.map((f) => [f.key, num(c[f.key])])) as ConfigForm
}

/** Valida e converte o formulário. `values` só vem preenchido quando não há erros. */
export function parseConfigForm(form: ConfigForm) {
  const errors: Partial<Record<keyof ConfigCalculo, string>> = {}
  const values = {} as ConfigCalculo
  for (const f of configFields) {
    const n = parseDecimal(form[f.key])
    if (n == null || n < 0) errors[f.key] = 'Informe um número maior ou igual a zero.'
    else values[f.key] = n
  }
  if (!errors.moedasPorK && values.moedasPorK <= 0) errors.moedasPorK = 'Precisa ser maior que zero.'
  if (!errors.valorKFeira && values.valorKFeira <= 0) errors.valorKFeira = 'Precisa ser maior que zero.'
  return { errors, values: Object.keys(errors).length ? null : values }
}

const configLabels: Record<string, string> = {
  valor_k_feira: 'Valor do K na feira',
  valor_k_trocas: 'Valor do K em trocas',
  moedas_por_k: 'Moedas por K',
  dolar_padrao_compra: 'Dólar padrão de compra',
  dolar_padrao_venda: 'Dólar padrão de venda',
  taxa_prata: 'Taxa de prata',
  margem_venda_cambios_percent: 'Margem de venda em câmbios (%)',
}

/** O que mudou entre duas versões do histórico, em frases curtas. */
export function diffPayload(antes: PayloadPrecificacao, depois: PayloadPrecificacao): string[] {
  const out: string[] = []
  const a = antes.configuracao_calculo as unknown as Record<string, number>
  const d = depois.configuracao_calculo as unknown as Record<string, number>
  for (const [key, label] of Object.entries(configLabels)) {
    if (a[key] !== d[key]) out.push(`${label}: ${formatDecimal(a[key])} → ${formatDecimal(d[key])}`)
  }
  const fa = antes.faixas_pix ?? []
  const fd = depois.faixas_pix ?? []
  if (JSON.stringify(fa) !== JSON.stringify(fd)) {
    out.push(fa.length === fd.length ? `Faixas PIX alteradas (${fd.length})` : `Faixas PIX: ${fa.length} → ${fd.length}`)
  }
  return out
}
