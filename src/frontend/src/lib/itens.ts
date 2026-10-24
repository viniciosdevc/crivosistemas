import { calculateFromUsd } from '@/lib/calculator'
import type { ItemEstoque, RegrasPrecificacao } from '@/lib/types'

/** Custo, preço de mercado e lucro de um item (totais, todas as unidades), pelas regras salvas. Tudo estimativa de tela. */
export function analisarItem(item: Pick<ItemEstoque, 'custoUnitario' | 'precoMercadoUsd' | 'quantidade'>, regras: RegrasPrecificacao) {
  const q = item.quantidade
  const custo = calculateFromUsd(item.custoUnitario, regras.configuracao, regras.faixasPix)
  const base = { custoUsd: item.custoUnitario * q, custoBrl: custo.custoBrl * q, custoCambios: custo.custoCambios * q }
  if (item.precoMercadoUsd == null) return { ...base, mercado: null }
  const m = calculateFromUsd(item.precoMercadoUsd, regras.configuracao, regras.faixasPix)
  const k = Math.max(regras.configuracao.moedasPorK, 0.0001)
  const cambiosBrl = (cambios: number) => (cambios * regras.configuracao.valorKFeira) / k
  const vendaPix = m.precoVendaPix * q
  const vendaCambios = m.precoVendaCambios * q
  return {
    ...base,
    mercado: {
      usd: item.precoMercadoUsd * q,
      diferencaUsd: (item.precoMercadoUsd - item.custoUnitario) * q,
      diferencaPercent: item.custoUnitario > 0 ? ((item.precoMercadoUsd - item.custoUnitario) / item.custoUnitario) * 100 : null,
      vendaPix,
      vendaCambios,
      lucroPix: vendaPix - base.custoBrl,
      lucroCambios: vendaCambios - base.custoCambios,
      lucroCambiosBrl: cambiosBrl(vendaCambios - base.custoCambios),
    },
  }
}
