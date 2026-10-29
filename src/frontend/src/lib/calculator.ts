import type { ConfigCalculo, FaixaPix } from './types'

/** Faixas usadas quando nenhuma está ativa (mesmas do crivonft). */
export const FAIXAS_PADRAO: FaixaPix[] = [
  { valorMinUsd: 0, valorMaxUsd: 10, multiplicador: 6.2, ordem: 0, ativo: true },
  { valorMinUsd: 10.01, valorMaxUsd: 15, multiplicador: 6, ordem: 1, ativo: true },
  { valorMinUsd: 15.01, valorMaxUsd: 25, multiplicador: 6, ordem: 2, ativo: true },
  { valorMinUsd: 25.01, valorMaxUsd: null, multiplicador: 5.9, ordem: 3, ativo: true },
]

/** Quantidade de esmeraldas que forma 1 K (padrão do mercado). */
export const ESMERALDAS_POR_K = 1000

/** Dólar de compra usado para precificar: o fixo configurado, nunca o custo médio do estoque. */
export function dolarCompraParaCalculo(settings: ConfigCalculo) {
  return settings.dolarPadraoCompra
}

export function roundMoney(value: number) {
  return Math.round((Number.isFinite(value) ? value : 0) * 100) / 100
}

export function roundCambios(value: number) {
  return Math.round(Number.isFinite(value) ? value : 0)
}

/** Só faixas ativas contam; sem nenhuma ativa, valem as padrão. */
function faixasAtivas(ranges: FaixaPix[]) {
  const active = ranges.filter((r) => r.ativo)
  return active.length > 0 ? active : FAIXAS_PADRAO
}

export function findPixRange(custoUsd: number, ranges: FaixaPix[]): FaixaPix {
  const sorted = [...faixasAtivas(ranges)].sort((a, b) => a.valorMinUsd - b.valorMinUsd)
  return (
    sorted.find((range) => custoUsd >= range.valorMinUsd && (range.valorMaxUsd === null || custoUsd <= range.valorMaxUsd)) ??
    sorted.at(-1) ??
    FAIXAS_PADRAO.at(-1)!
  )
}

/**
 * Fonte única de precificação. Custo USD → dólar de VENDA → custo base em câmbios →
 * + taxa de prata (custo obrigatório) → margem → preço em câmbios / PIX.
 * O PIX é SEMPRE o preço da faixa: custo nunca altera o preço de venda.
 */
export function calculateFromUsd(custoUsd: number, settings: ConfigCalculo, ranges: FaixaPix[], dolarCompra = dolarCompraParaCalculo(settings)) {
  const custoBrl = custoUsd * dolarCompra
  const custoCambiosBase = (custoBrl * settings.moedasPorK) / Math.max(settings.valorKFeira, 0.0001)
  const custoCambios = custoCambiosBase + settings.taxaPrata

  const precoBaseBrl = custoUsd * settings.dolarPadraoVenda
  const precoCambiosBase = (precoBaseBrl * settings.moedasPorK) / Math.max(settings.valorKFeira, 0.0001)
  const custoTotalCambios = precoCambiosBase + settings.taxaPrata
  // Arredonda para CIMA: o arredondamento nunca derruba o preço abaixo de custo + margem.
  const precoVendaCambios = Math.ceil(custoTotalCambios * (1 + settings.margemVendaCambiosPercent / 100))

  const custoTotalComPrataBrl = (custoTotalCambios * settings.valorKFeira) / Math.max(settings.moedasPorK, 0.0001)

  const pixRange = findPixRange(custoUsd, ranges)
  const precoVendaPixFaixa = custoUsd * pixRange.multiplicador
  const precoVendaPix = precoVendaPixFaixa

  return {
    custoUsd: roundMoney(custoUsd),
    custoBrl: roundMoney(custoBrl),
    custoCambios: roundCambios(custoCambios),
    custoCambiosBase: roundCambios(custoCambiosBase),
    dolarCompra: roundMoney(dolarCompra),
    dolarVenda: roundMoney(settings.dolarPadraoVenda),
    precoBaseBrl: roundMoney(precoBaseBrl),
    precoCambiosBase: roundCambios(precoCambiosBase),
    precoCambiosCusto: roundCambios(custoTotalCambios),
    custoTotalComPrataBrl: roundMoney(custoTotalComPrataBrl),
    precoVendaCambios,
    precoVendaPix: roundMoney(precoVendaPix),
    precoVendaPixFaixa: roundMoney(precoVendaPixFaixa),
    pixAjustadoPeloPiso: false,
    pixMultiplicador: pixRange.multiplicador,
  }
}

/** Esmeralda → custo em USD (K cotado em dólar: K $ 9,44 = 1.000 esm, 300 esm = $ 2,83). */
export function esmeraldasParaUsd(quantidade: number, valorKEsmeraldaUsd: number, esmeraldasPorK = ESMERALDAS_POR_K) {
  return (quantidade * valorKEsmeraldaUsd) / Math.max(esmeraldasPorK, 0.0001)
}

export function calculateFromCambios(custoCambios: number, settings: ConfigCalculo) {
  const custoBrl = (custoCambios * settings.valorKFeira) / Math.max(settings.moedasPorK, 0.0001)
  const precoVendaCambios = custoCambios * (1 + settings.margemVendaCambiosPercent / 100)
  return {
    custoUsd: 0,
    custoBrl: roundMoney(custoBrl),
    custoCambios: roundCambios(custoCambios),
    // Custo manual em câmbios já é o valor final: sem conversão de dólar nem taxa de prata.
    custoCambiosBase: roundCambios(custoCambios),
    dolarCompra: null as number | null,
    dolarVenda: null as number | null,
    precoBaseBrl: 0,
    precoCambiosBase: roundCambios(custoCambios),
    precoCambiosCusto: roundCambios(custoCambios),
    precoVendaCambios: roundCambios(precoVendaCambios),
    precoVendaPix: 0,
    pixMultiplicador: 0,
  }
}

export interface EntradaVenda {
  custoUsd: number
  valorVendaPix: number
  valorVendaCambios: number
  settings: ConfigCalculo
  ranges: FaixaPix[]
  custoCambiosManual?: number
  dolarCompra?: number
}

export function calculateSale(input: EntradaVenda) {
  const base =
    input.custoCambiosManual !== undefined
      ? calculateFromCambios(input.custoCambiosManual, input.settings)
      : calculateFromUsd(input.custoUsd, input.settings, input.ranges, input.dolarCompra)

  const cambioToBrl = (input.valorVendaCambios * input.settings.valorKFeira) / Math.max(input.settings.moedasPorK, 0.0001)
  const receitaTotalBrl = input.valorVendaPix + cambioToBrl
  // custoCambios já inclui a taxa de prata; converte de volta para BRL para ter o custo completo.
  const custoBrlTotal = (base.custoCambios * input.settings.valorKFeira) / Math.max(input.settings.moedasPorK, 0.0001)
  const custoBrlBase = custoBrlTotal > 0 ? custoBrlTotal : base.custoBrl
  const lucroBrl = receitaTotalBrl - custoBrlBase
  const lucroCambios =
    input.valorVendaCambios + (input.valorVendaPix * input.settings.moedasPorK) / Math.max(input.settings.valorKFeira, 0.0001) - base.custoCambios
  const margemPercent = custoBrlBase > 0 ? (lucroBrl / custoBrlBase) * 100 : 0
  const taxaPrataAplicada = base.custoCambios - base.custoCambiosBase

  return {
    ...base,
    custoBrl: roundMoney(custoBrlBase),
    custoBrlSemTaxa: roundMoney(base.custoBrl),
    custoCambiosBase: roundCambios(base.custoCambiosBase),
    taxaPrataAplicada: roundCambios(taxaPrataAplicada),
    dolarCompraUsado: base.dolarCompra,
    receitaTotalBrl: roundMoney(receitaTotalBrl),
    lucroBrl: roundMoney(lucroBrl),
    lucroCambios: roundCambios(lucroCambios),
    margemPercent: roundMoney(margemPercent),
  }
}

/** Dólar de compra pelo estoque (FIFO): consome o lote mais antigo primeiro. `lotes` vem do mais antigo ao mais novo. O que passar do saldo usa o `reserva`. */
export function dolarFifo(lotes: { saldo: number; custoUnitario: number }[], qtdUsd: number, reserva: number) {
  if (qtdUsd <= 0) return reserva
  let resta = qtdUsd
  let brl = 0
  for (const l of lotes) {
    if (resta <= 0) break
    const d = Math.min(l.saldo, resta)
    brl += d * l.custoUnitario
    resta -= d
  }
  brl += Math.max(resta, 0) * reserva
  return brl / qtdUsd
}
