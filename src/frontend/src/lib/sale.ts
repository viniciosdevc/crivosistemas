import { calculateSale } from './calculator'
import { parseDecimal } from './format'
import type { ConfigCalculo, FormaPagamento, OrigemCusto, RegrasPrecificacao } from './types'

/** Campos do formulário de venda manual como o usuário digita (texto). */
export interface EntradaVendaManual {
  forma: FormaPagamento
  origem: OrigemCusto
  custoUsd: string
  custoCambios: string
  quantidadeEsmeraldas: string
  usaTaxaPrata: boolean
  valorPix: string
  valorCambios: string
}

export interface CustosEstoque {
  /** R$ por US$ (custo médio do estoque de dólar). */
  usdCustoMedio: number
  /** US$ por esmeralda (custo médio do estoque de esmeraldas). */
  esmeraldasCustoMedio: number
}

const num = (raw: string) => Math.max(parseDecimal(raw) ?? 0, 0)

/**
 * Mesma regra do servidor: dólar do estoque pelo custo médio (se houver), esmeraldas pelo custo médio delas em US$,
 * taxa de prata zerada quando desmarcada. O servidor recalcula tudo ao salvar; isto é só a estimativa na tela.
 */
export function calcularVendaManual(input: EntradaVendaManual, regras: RegrasPrecificacao, custos: CustosEstoque) {
  const usaEsmeralda = input.origem === 'estoque_esmeralda'
  const usaCambios = input.origem === 'cambios'
  const qtdEsmeraldas = usaEsmeralda ? num(input.quantidadeEsmeraldas) : 0
  const custoCambios = usaCambios ? Math.round(num(input.custoCambios)) : 0

  let custoUsd = 0
  if (usaEsmeralda) custoUsd = qtdEsmeraldas * custos.esmeraldasCustoMedio
  else if (!usaCambios) custoUsd = num(input.custoUsd)

  const dolarCompra = input.origem === 'estoque_usd' && custos.usdCustoMedio > 0 ? custos.usdCustoMedio : regras.configuracao.dolarPadraoCompra
  const valorPix = input.forma === 'cambios' ? 0 : num(input.valorPix)
  const valorCambios = input.forma === 'pix' ? 0 : Math.round(num(input.valorCambios))

  const result = calculateSale({
    custoUsd,
    valorVendaPix: valorPix,
    valorVendaCambios: valorCambios,
    settings: input.usaTaxaPrata ? regras.configuracao : { ...regras.configuracao, taxaPrata: 0 },
    ranges: regras.faixasPix,
    custoCambiosManual: usaCambios ? custoCambios : undefined,
    dolarCompra,
  })

  // Margem ideal de referência: câmbios usa a margem configurada; PIX usa o multiplicador da faixa sobre o dólar de compra.
  const margemIdeal =
    usaCambios || input.forma === 'cambios'
      ? regras.configuracao.margemVendaCambiosPercent
      : result.pixMultiplicador > 0 && dolarCompra > 0
        ? (result.pixMultiplicador / dolarCompra - 1) * 100
        : regras.configuracao.margemVendaCambiosPercent

  return { custoUsd, dolarCompra, valorPix, valorCambios, qtdEsmeraldas, custoCambios, result, margemIdeal, abaixoDoIdeal: result.margemPercent < margemIdeal }
}

/** Texto digitado e pronto para enviar? O servidor valida de novo. */
export function vendaManualValida(input: EntradaVendaManual, nomeItem: string) {
  if (!nomeItem.trim()) return false
  const pix = input.forma === 'cambios' ? 0 : num(input.valorPix)
  const cambios = input.forma === 'pix' ? 0 : num(input.valorCambios)
  if (pix + cambios <= 0) return false
  if (input.origem === 'estoque_esmeralda' && num(input.quantidadeEsmeraldas) <= 0) return false
  if (input.origem === 'cambios' && num(input.custoCambios) <= 0) return false
  return true
}

/** Corpo do POST /api/vendas/manuais a partir do que foi digitado. */
export function montarVendaManual(input: EntradaVendaManual, extra: { dataHora?: string; nickname: string; nomeItem: string; observacao: string; entregue: boolean }) {
  const usaEsmeralda = input.origem === 'estoque_esmeralda'
  const usaCambios = input.origem === 'cambios'
  return {
    ...(extra.dataHora ? { dataHora: extra.dataHora } : {}),
    nicknameComprador: extra.nickname.trim(),
    nomeItem: extra.nomeItem.trim(),
    formaPagamento: input.forma,
    origemCusto: input.origem,
    custoUsd: usaEsmeralda || usaCambios ? 0 : num(input.custoUsd),
    custoCambios: usaCambios ? Math.round(num(input.custoCambios)) : 0,
    quantidadeEsmeraldas: usaEsmeralda ? num(input.quantidadeEsmeraldas) : 0,
    usaTaxaPrata: input.usaTaxaPrata,
    valorVendaPix: input.forma === 'cambios' ? 0 : num(input.valorPix),
    valorVendaCambios: input.forma === 'pix' ? 0 : Math.round(num(input.valorCambios)),
    observacao: extra.observacao.trim(),
    entregue: extra.entregue,
  }
}

/** Dia (AAAA-MM-DD) e hora (HH:MM) no fuso `timeZone` → instante ISO em UTC. */
export function zonedToIso(day: string, time: string, timeZone: string) {
  const [y, mo, d] = day.split('-').map(Number)
  const [h, mi] = time.split(':').map(Number)
  const wanted = Date.UTC(y!, mo! - 1, d!, h!, mi!)
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en-US', { timeZone, hourCycle: 'h23', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
      .formatToParts(new Date(wanted))
      .map((p) => [p.type, p.value]),
  )
  const asSeen = Date.UTC(Number(parts.year), Number(parts.month) - 1, Number(parts.day), Number(parts.hour), Number(parts.minute))
  // O instante real é o desejado menos o deslocamento do fuso naquele momento.
  return new Date(wanted - (asSeen - wanted)).toISOString()
}

/** Taxa da feira (pratas) em R$: a taxa configurada está em câmbios, convertida pelo K da feira. */
export function taxaFeiraBrl(config: Pick<ConfigCalculo, 'taxaPrata' | 'valorKFeira' | 'moedasPorK'>) {
  return (config.taxaPrata * config.valorKFeira) / Math.max(config.moedasPorK, 0.0001)
}
