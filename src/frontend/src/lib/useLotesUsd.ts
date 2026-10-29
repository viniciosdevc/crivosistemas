import { onMounted, ref } from 'vue'
import { api } from '@/lib/api'
import type { LoteEstoque, Paged } from '@/lib/types'

/** Lotes de USD com saldo, do mais antigo ao mais novo (base do custo médio). */
export function useLotesUsd() {
  const lotes = ref<{ saldo: number; custoUnitario: number }[]>([])
  onMounted(async () => {
    try {
      const r = await api.get<Paged<LoteEstoque>>('/api/estoque/usd/lotes', { page: 1, pageSize: 200 })
      lotes.value = r.items.filter((l) => l.saldo > 0).reverse().map((l) => ({ saldo: l.saldo, custoUnitario: l.custoUnitario }))
    } catch {
      lotes.value = []
    }
  })
  return lotes
}
