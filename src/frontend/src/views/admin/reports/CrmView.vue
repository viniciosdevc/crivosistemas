<script setup lang="ts">
import { Star, TriangleAlert } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import RankedList, { type RankedRow } from '@/components/admin/reports/RankedList.vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import { formatDate, formatInt, formatMoney, formatPercent, pluralize } from '@/lib/format'
import { segmentoCliente } from '@/lib/labels'
import { toast } from '@/lib/toast'
import type { AnaliseCrm, ClienteCrm } from '@/lib/types'
import { useUrlFilters } from '@/lib/urlFilters'
import { useAsync } from '@/lib/useAsync'

const { filters, set } = useUrlFilters({ dias: '60' })
const dias = computed(() => (['30', '60', '90', '180'].includes(filters.value.dias) ? Number(filters.value.dias) : 60))
const diasOptions = [30, 60, 90, 180].map((d) => ({ value: String(d), label: `${d} dias` }))

const { data, error, run } = useAsync((signal) => api.get<AnaliseCrm>('/api/clientes/crm', { inativoDias: dias.value }, signal))
watch(dias, () => void run(), { immediate: true })
watch(error, (e) => e && toast.error('Não foi possível carregar a análise', e.message))

const a = computed(() => data.value?.analise)
const stats = computed<Stat[]>(() => {
  const t = a.value?.totais
  if (!t) return []
  return [
    { label: 'Receita de clientes', value: t.receita, note: `${pluralize(t.compradores, 'comprador', 'compradores')} de ${formatInt(t.clientes)}` },
    { label: 'Ticket médio', value: t.ticketMedio, note: `${t.comprasPorCliente.toFixed(1).replace('.', ',')} compras por cliente` },
    { label: 'LTV médio', value: t.ltvMedio, note: `Recompra: ${formatPercent(t.taxaRecompra)}` },
    { label: 'Top 10 concentram', text: formatPercent(t.concentracaoTop10), note: 'da receita total' },
    { label: 'Desde a última compra', text: `${Math.round(t.diasMedioDesdeUltima)} dias`, note: 'média dos compradores' },
  ]
})
const segmentos = computed<RankedRow[]>(() => {
  const list = a.value?.segmentos ?? []
  const total = list.reduce((s, x) => s + x.clientes, 0)
  return list.map((s) => {
    const meta = segmentoCliente[s.segmento]
    const share = total > 0 ? (s.clientes / total) * 100 : 0
    return { key: s.segmento, label: meta?.label ?? s.segmento, value: pluralize(s.clientes, 'cliente', 'clientes'), share, note: `${formatMoney(s.receita)} · ${meta?.hint ?? ''}` }
  })
})
const columns: Column[] = [
  { key: 'cliente', label: 'Cliente' },
  { key: 'segmento', label: 'Segmento', hideBelow: 'sm' },
  { key: 'totalGasto', label: 'Total gasto', align: 'right' },
  { key: 'compras', label: 'Compras', align: 'right', hideBelow: 'md' },
  { key: 'dias', label: 'Sem comprar', align: 'right' },
  { key: 'ultimaCompra', label: 'Última compra', hideBelow: 'lg' },
]
const go = (c: ClienteCrm) => ({ name: 'customer', params: { id: c.id } })
</script>

<template>
  <PageCard title="CRM de clientes" subtitle="Quem compra, quem voltou, quem está sumindo e quem vale reativar.">
    <div class="mb-6 flex flex-wrap items-center gap-3">
      <span class="text-sm text-muted">Em risco após</span>
      <UiChips :model-value="String(dias)" :options="diasOptions" label="Prazo sem comprar" @update:model-value="(v) => set({ dias: v })" />
    </div>

    <UiEmptyState v-if="error && !data" :icon="TriangleAlert" title="Não foi possível carregar a análise" :description="error.message" />
    <template v-else>
      <StatBlocks :stats="stats" :loading="!a" :skeletons="5" />

      <div class="mt-8 grid gap-8 lg:grid-cols-3">
        <section aria-labelledby="seg-title">
          <h2 id="seg-title" class="mb-4 text-lg font-semibold text-ink">Segmentos</h2>
          <UiSkeleton v-if="!a" class="h-40 w-full" />
          <RankedList v-else :rows="segmentos" label="Clientes por segmento" />
        </section>
        <section class="lg:col-span-2" aria-labelledby="top-title">
          <h2 id="top-title" class="mb-4 text-lg font-semibold text-ink">Top 10 por gasto</h2>
          <UiSkeleton v-if="!a" class="h-40 w-full" />
          <DataTable v-else :columns="columns" :rows="a.top" :row-key="(c: ClienteCrm) => c.id" :row-to="go" caption="Maiores clientes">
            <template #cell-cliente="{ row }"><span class="inline-flex items-center gap-1.5 font-medium text-ink"><Star v-if="row.favorito" class="size-4 fill-current text-warning" aria-label="Favorito" />{{ row.nickname }}</span></template>
            <template #cell-segmento="{ row }"><UiBadge :tone="segmentoCliente[row.segmento]?.tone">{{ segmentoCliente[row.segmento]?.label }}</UiBadge></template>
            <template #cell-totalGasto="{ row }"><span class="font-medium text-ink tabular">{{ formatMoney(row.totalGasto) }}</span></template>
            <template #cell-compras="{ row }"><span class="tabular">{{ formatInt(row.compras) }}</span></template>
            <template #cell-dias="{ row }"><span class="tabular">{{ row.diasSemComprar ?? '—' }} d</span></template>
            <template #cell-ultimaCompra="{ row }">{{ formatDate(row.ultimaCompra) }}</template>
            <template #empty><UiEmptyState title="Sem compras ainda" description="Os clientes aparecem aqui quando houver vendas pagas." /></template>
          </DataTable>
        </section>
      </div>

      <section class="mt-8" aria-labelledby="re-title">
        <h2 id="re-title" class="text-lg font-semibold text-ink">Para reativar</h2>
        <p class="mb-4 text-sm text-muted">Já compraram e pararam há mais de {{ dias }} dias. Favoritos primeiro, depois quem mais gastou.</p>
        <UiSkeleton v-if="!a" class="h-40 w-full" />
        <DataTable v-else :columns="columns" :rows="a.reativar" :row-key="(c: ClienteCrm) => c.id" :row-to="go" caption="Clientes para reativar">
          <template #cell-cliente="{ row }"><span class="inline-flex items-center gap-1.5 font-medium text-ink"><Star v-if="row.favorito" class="size-4 fill-current text-warning" aria-label="Favorito" />{{ row.nickname }}<UiBadge v-if="row.vip" tone="brand">VIP</UiBadge></span></template>
          <template #cell-segmento="{ row }"><UiBadge :tone="segmentoCliente[row.segmento]?.tone">{{ segmentoCliente[row.segmento]?.label }}</UiBadge></template>
          <template #cell-totalGasto="{ row }"><span class="font-medium text-ink tabular">{{ formatMoney(row.totalGasto) }}</span></template>
          <template #cell-compras="{ row }"><span class="tabular">{{ formatInt(row.compras) }}</span></template>
          <template #cell-dias="{ row }"><span class="tabular">{{ row.diasSemComprar ?? '—' }} d</span></template>
          <template #cell-ultimaCompra="{ row }">{{ formatDate(row.ultimaCompra) }}</template>
          <template #empty><UiEmptyState title="Ninguém para reativar" description="Todos os compradores estão dentro do prazo." /></template>
        </DataTable>
      </section>
    </template>
  </PageCard>
</template>
