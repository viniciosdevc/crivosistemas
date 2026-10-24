<script setup lang="ts">
import { Banknote, Plus, RefreshCw, XCircle } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import ItemModal from '@/components/admin/stock/ItemModal.vue'
import SellItemModal from '@/components/admin/stock/SellItemModal.vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import { ApiError, api } from '@/lib/api'
import { confirmWithReason } from '@/lib/confirm'
import { formatDay, formatMoney } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { ItemEstoque, Paged, ResumoItensEstoque, StatusItemEstoque } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const pageSize = 10
const status = ref<StatusItemEstoque>('estoque')
const page = ref(1)
const statusTabs = [
  { value: 'estoque' as const, label: 'Em estoque' },
  { value: 'vendido' as const, label: 'Vendidos' },
  { value: 'cancelado' as const, label: 'Cancelados' },
]

const resumo = useAsync((signal) => api.get<ResumoItensEstoque>('/api/estoque/itens/resumo', undefined, signal))
const itens = useAsync((signal) => api.get<Paged<ItemEstoque>>('/api/estoque/itens', { status: status.value, page: page.value, pageSize }, signal))
watch(status, () => (page.value = 1))
watch([status, page], () => void itens.run(), { immediate: true })
void resumo.run()
for (const a of [resumo, itens]) watch(a.error, (e) => e && toast.error('Não foi possível carregar os itens', e.message))

function refresh() {
  void resumo.run()
  void itens.run()
}

const stats = computed<Stat[]>(() => {
  const r = resumo.data.value
  if (!r) return []
  return [
    { label: 'Em estoque', count: true, value: r.unidades, note: `${r.itens} ${r.itens === 1 ? 'item' : 'itens'}` },
    { label: 'Custo', value: r.custoTotal },
    { label: 'Valor de mercado', ...(r.valorMercado != null ? { value: r.valorMercado } : { text: '—' }), note: r.semPreco ? `${r.semPreco} sem preço` : undefined },
    { label: 'Lucro potencial', ...(r.lucroPotencial != null ? { value: r.lucroPotencial } : { text: '—' }), tone: (r.lucroPotencial ?? 0) < 0 ? 'danger' : 'success' },
    { label: 'Lucro realizado', value: r.lucroRealizado, note: `${r.vendidos} vendido(s)`, tone: r.lucroRealizado < 0 ? 'danger' : 'success' },
  ]
})

const columns = computed<Column[]>(() => [
  { key: 'nome', label: 'Item' },
  { key: 'quantidade', label: 'Qtd', align: 'right' },
  { key: 'custoUnitario', label: 'Pago (cada)', align: 'right', hideBelow: 'md' },
  ...(status.value === 'vendido'
    ? [{ key: 'valorVenda', label: 'Recebido', align: 'right' as const }, { key: 'lucroRealizado', label: 'Lucro', align: 'right' as const }]
    : status.value === 'estoque'
      ? [{ key: 'precoMercadoBrl', label: 'Mercado (cada)', align: 'right' as const, hideBelow: 'md' as const }, { key: 'lucroPotencial', label: 'Lucro potencial', align: 'right' as const }]
      : [{ key: 'motivoCancelamento', label: 'Motivo', hideBelow: 'md' as const }]),
  { key: 'acoes', label: '', align: 'right' },
])

const addOpen = ref(false)
const selling = ref<ItemEstoque | null>(null)
const sellOpen = ref(false)
function sell(i: ItemEstoque) { selling.value = i; sellOpen.value = true }

async function cancelItem(i: ItemEstoque) {
  const motivo = await confirmWithReason({
    title: 'Cancelar este item?',
    description: `${i.nome} sai do estoque e fica no histórico como cancelado.`,
    confirmLabel: 'Cancelar item',
    danger: true,
  })
  if (motivo === null) return
  try {
    await api.post(`/api/estoque/itens/${i.id}/cancelar`, { motivo })
    toast.success('Item cancelado')
    refresh()
  } catch (e) {
    toast.error('Não foi possível cancelar o item', e instanceof ApiError ? e.message : undefined)
  }
}

const updating = ref(false)
async function updatePrices() {
  updating.value = true
  try {
    const r = await api.post<{ atualizados: number; total: number }>('/api/estoque/itens/atualizar-precos')
    toast.success('Preços atualizados', `${r.atualizados} de ${r.total} item(ns) com preço do catálogo.`)
    refresh()
  } catch (e) {
    toast.error('Não foi possível atualizar os preços', e instanceof ApiError ? e.message : undefined)
  } finally {
    updating.value = false
  }
}

const tone = (v: number | null) => (v == null ? '' : v < 0 ? 'text-danger' : 'text-success')
</script>

<template>
  <div class="flex flex-col gap-8">
    <div class="flex flex-wrap items-center gap-3">
      <UiButton @click="addOpen = true"><Plus class="size-4" /> Adicionar item</UiButton>
      <UiButton variant="secondary" :loading="updating" @click="updatePrices"><RefreshCw class="size-4" /> Atualizar preços</UiButton>
    </div>

    <StatBlocks :stats="stats" :loading="!resumo.data.value" :skeletons="5" />

    <section aria-labelledby="items-title">
      <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 id="items-title" class="text-lg font-semibold text-ink">Itens</h2>
        <UiTabs v-model="status" :tabs="statusTabs" />
      </div>
      <DataTable :columns="columns" :rows="itens.data.value?.items" :row-key="(i) => i.id" :loading="itens.loading.value" caption="Itens do estoque">
        <template #cell-nome="{ row }">
          <div class="flex items-center gap-3">
            <img v-if="row.imagemUrl" :src="row.imagemUrl" :alt="row.nome" class="size-10 shrink-0 rounded-xl bg-surface-3 object-contain" loading="lazy" />
            <span v-else class="size-10 shrink-0 rounded-xl bg-surface-3" />
            <div class="min-w-0">
              <p class="truncate font-medium text-ink">{{ row.nome }}</p>
              <p class="text-xs text-muted">{{ row.categoria ? `${row.categoria} · ` : '' }}{{ formatDay(row.dataCompra) }}</p>
            </div>
          </div>
        </template>
        <template #cell-quantidade="{ row }"><span class="tabular">{{ row.quantidade }}</span></template>
        <template #cell-custoUnitario="{ row }"><span class="tabular">{{ formatMoney(row.custoUnitario) }}</span></template>
        <template #cell-precoMercadoBrl="{ row }"><span class="tabular">{{ row.precoMercadoBrl != null ? formatMoney(row.precoMercadoBrl) : '—' }}</span></template>
        <template #cell-lucroPotencial="{ row }"><span :class="['tabular font-medium', tone(row.lucroPotencial)]">{{ row.lucroPotencial != null ? formatMoney(row.lucroPotencial) : '—' }}</span></template>
        <template #cell-valorVenda="{ row }"><span class="tabular">{{ formatMoney(row.valorVenda) }}</span></template>
        <template #cell-lucroRealizado="{ row }"><span :class="['tabular font-medium', tone(row.lucroRealizado)]">{{ formatMoney(row.lucroRealizado) }}</span></template>
        <template #cell-motivoCancelamento="{ row }"><span class="text-sm text-muted">{{ row.motivoCancelamento ?? '—' }}</span></template>
        <template #cell-acoes="{ row }">
          <div v-if="row.status === 'estoque'" class="flex justify-end gap-1">
            <UiButton variant="ghost" size="icon-sm" :aria-label="`Vender ${row.nome}`" title="Vender" @click="sell(row)"><Banknote class="size-4" /></UiButton>
            <UiButton variant="ghost" size="icon-sm" :aria-label="`Cancelar ${row.nome}`" title="Cancelar" @click="cancelItem(row)"><XCircle class="size-4 text-danger" /></UiButton>
          </div>
        </template>
        <template #empty>
          <UiEmptyState title="Nenhum item aqui" description="Adicione um item pelo nome e informe quanto pagou." />
        </template>
        <template #footer>
          <UiPagination v-if="itens.data.value" :page="itens.data.value.page" :page-size="itens.data.value.pageSize" :total="itens.data.value.total" @update:page="(p) => (page = p)" />
        </template>
      </DataTable>
    </section>

    <ItemModal v-model:open="addOpen" @saved="refresh" />
    <SellItemModal v-model:open="sellOpen" :item="selling" @saved="refresh" />
  </div>
</template>
