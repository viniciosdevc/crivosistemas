<script setup lang="ts">
import { Banknote, Plus, RefreshCw, XCircle } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import ItemModal from '@/components/admin/stock/ItemModal.vue'
import SellItemModal from '@/components/admin/stock/SellItemModal.vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import { ApiError, api } from '@/lib/api'
import { confirmWithReason } from '@/lib/confirm'
import { formatDay, formatInt, formatMoney, formatUsd } from '@/lib/format'
import { analisarItem } from '@/lib/itens'
import { toast } from '@/lib/toast'
import type { ItemEstoque, Paged, RegrasPrecificacao, StatusItemEstoque } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const status = ref<StatusItemEstoque>('estoque')
const statusTabs = [
  { value: 'estoque' as const, label: 'Em estoque' },
  { value: 'vendido' as const, label: 'Vendidos' },
  { value: 'cancelado' as const, label: 'Cancelados' },
]

const regras = useAsync((signal) => api.get<RegrasPrecificacao>('/api/precificacao/regras', undefined, signal))
const todos = useAsync((signal) => api.get<Paged<ItemEstoque>>('/api/estoque/itens', { pageSize: 200 }, signal))
for (const a of [regras, todos]) watch(a.error, (e) => e && toast.error('Não foi possível carregar os itens', e.message))

const itens = computed(() => (todos.data.value?.items ?? []).filter((i) => i.status === status.value))
const linhas = computed(() => itens.value.map((i) => ({ ...i, analise: regras.data.value ? analisarItem(i, regras.data.value) : null })))

// Sempre traz o preço atual do mercado ao abrir a tela, sem precisar clicar.
const updating = ref(false)
async function updatePrices(silencioso = false) {
  updating.value = true
  try {
    const r = await api.post<{ atualizados: number; total: number }>('/api/estoque/itens/atualizar-precos')
    if (!silencioso) toast.success('Preços atualizados', `${r.atualizados} de ${r.total} item(ns) com preço do catálogo.`)
    await todos.run()
  } catch (e) {
    if (!silencioso) toast.error('Não foi possível atualizar os preços', e instanceof ApiError ? e.message : undefined)
  } finally {
    updating.value = false
  }
}
void regras.run()
void todos.run().then(() => updatePrices(true))

function refresh() {
  void todos.run()
}

const stats = computed<Stat[]>(() => {
  if (!todos.data.value || !regras.data.value) return []
  const emEstoque = todos.data.value.items.filter((i) => i.status === 'estoque').map((i) => analisarItem(i, regras.data.value!))
  const vendidos = todos.data.value.items.filter((i) => i.status === 'vendido')
  const comPreco = emEstoque.filter((a) => a.mercado)
  const soma = (f: (a: (typeof emEstoque)[number]) => number) => emEstoque.reduce((t, a) => t + f(a), 0)
  const mercadoPix = comPreco.reduce((t, a) => t + a.mercado!.vendaPix, 0)
  const lucroPix = comPreco.reduce((t, a) => t + a.mercado!.lucroPix, 0)
  const lucroCambios = comPreco.reduce((t, a) => t + a.mercado!.lucroCambios, 0)
  const realizado = vendidos.reduce((t, i) => t + (i.valorVenda ?? 0) - analisarItem(i, regras.data.value!).custoBrl, 0)
  const semPreco = emEstoque.length - comPreco.length
  return [
    { label: 'Custo', text: formatUsd(soma((a) => a.custoUsd)), note: `${formatInt(soma((a) => a.custoCambios))} C · ${formatMoney(soma((a) => a.custoBrl))}` },
    { label: 'Valor de mercado (PIX)', ...(comPreco.length ? { value: mercadoPix } : { text: '—' }), note: semPreco ? `${semPreco} sem preço` : undefined },
    { label: 'Lucro potencial (PIX)', ...(comPreco.length ? { value: lucroPix } : { text: '—' }), tone: lucroPix < 0 ? 'danger' : 'success' },
    { label: 'Lucro potencial (câmbios)', text: comPreco.length ? `${formatInt(lucroCambios)} C` : '—', tone: lucroCambios < 0 ? 'danger' : 'success' },
    { label: 'Lucro realizado', value: realizado, note: `${vendidos.length} vendido(s)`, tone: realizado < 0 ? 'danger' : 'success' },
  ]
})

const columns = computed<Column[]>(() => [
  { key: 'nome', label: 'Item' },
  { key: 'quantidade', label: 'Qtd', align: 'right' },
  { key: 'custo', label: 'Custo', align: 'right' },
  ...(status.value === 'vendido'
    ? [{ key: 'valorVenda', label: 'Recebido', align: 'right' as const }, { key: 'lucroRealizado', label: 'Lucro', align: 'right' as const }]
    : status.value === 'estoque'
      ? [
          { key: 'mercado', label: 'Mercado agora', align: 'right' as const, hideBelow: 'md' as const },
          { key: 'diferenca', label: 'Diferença', align: 'right' as const, hideBelow: 'lg' as const },
          { key: 'lucro', label: 'Lucro potencial', align: 'right' as const },
        ]
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

const tone = (v: number | null) => (v == null ? '' : v < 0 ? 'text-danger' : 'text-success')
</script>

<template>
  <div class="flex flex-col gap-8">
    <div class="flex flex-wrap items-center gap-3">
      <UiButton @click="addOpen = true"><Plus class="size-4" /> Adicionar item</UiButton>
      <UiButton variant="secondary" :loading="updating" @click="updatePrices(false)"><RefreshCw class="size-4" /> Atualizar preços</UiButton>
    </div>

    <StatBlocks :stats="stats" :loading="!todos.data.value || !regras.data.value" :skeletons="5" />

    <section aria-labelledby="items-title">
      <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 id="items-title" class="text-lg font-semibold text-ink">Itens</h2>
        <UiTabs v-model="status" :tabs="statusTabs" />
      </div>
      <DataTable :columns="columns" :rows="linhas" :row-key="(i) => i.id" :loading="todos.loading.value && !todos.data.value" caption="Itens do estoque">
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
        <template #cell-custo="{ row }">
          <p class="tabular">{{ formatUsd(row.custoTotal) }}</p>
          <p v-if="row.analise" class="text-xs text-muted tabular">{{ formatInt(row.analise.custoCambios) }} C · {{ formatMoney(row.analise.custoBrl) }}</p>
        </template>
        <template #cell-mercado="{ row }">
          <template v-if="row.analise?.mercado">
            <p class="tabular">{{ formatUsd(row.analise.mercado.usd) }}</p>
            <p class="text-xs text-muted tabular">{{ formatMoney(row.analise.mercado.vendaPix) }} PIX · {{ formatInt(row.analise.mercado.vendaCambios) }} C</p>
          </template>
          <span v-else class="text-muted">—</span>
        </template>
        <template #cell-diferenca="{ row }">
          <template v-if="row.analise?.mercado">
            <p :class="['tabular font-medium', tone(row.analise.mercado.diferencaUsd)]">{{ formatUsd(row.analise.mercado.diferencaUsd) }}</p>
            <p v-if="row.analise.mercado.diferencaPercent != null" class="text-xs text-muted tabular">{{ row.analise.mercado.diferencaPercent.toFixed(1).replace('.', ',') }}%</p>
          </template>
          <span v-else class="text-muted">—</span>
        </template>
        <template #cell-lucro="{ row }">
          <template v-if="row.analise?.mercado">
            <p :class="['tabular font-medium', tone(row.analise.mercado.lucroPix)]">{{ formatMoney(row.analise.mercado.lucroPix) }}</p>
            <p :class="['text-xs tabular', tone(row.analise.mercado.lucroCambios)]">{{ formatInt(row.analise.mercado.lucroCambios) }} C</p>
          </template>
          <span v-else class="text-muted">—</span>
        </template>
        <template #cell-valorVenda="{ row }"><span class="tabular">{{ formatMoney(row.valorVenda) }}</span></template>
        <template #cell-lucroRealizado="{ row }">
          <span v-if="row.analise" :class="['tabular font-medium', tone((row.valorVenda ?? 0) - row.analise.custoBrl)]">{{ formatMoney((row.valorVenda ?? 0) - row.analise.custoBrl) }}</span>
        </template>
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
      </DataTable>
    </section>

    <ItemModal v-model:open="addOpen" :regras="regras.data.value" @saved="refresh" />
    <SellItemModal v-model:open="sellOpen" :item="selling" :regras="regras.data.value" @saved="refresh" />
  </div>
</template>
