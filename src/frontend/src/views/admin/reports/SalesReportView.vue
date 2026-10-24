<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import RankedList, { type RankedRow } from '@/components/admin/reports/RankedList.vue'
import SalesChart from '@/components/admin/dashboard/SalesChart.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import { formatMoney, formatPercent, pluralize } from '@/lib/format'
import { origemVenda } from '@/lib/labels'
import { isPeriodPreset, periodOptions, resolvePeriod, type PeriodPreset } from '@/lib/period'
import { toast } from '@/lib/toast'
import type { ResumoVendas } from '@/lib/types'
import { useUrlFilters } from '@/lib/urlFilters'
import { useAsync } from '@/lib/useAsync'

const { filters, set } = useUrlFilters({ periodo: '30d', de: '', ate: '' })
const preset = computed<PeriodPreset>(() => (isPeriodPreset(filters.value.periodo) ? filters.value.periodo : '30d'))
const range = computed(() => resolvePeriod(preset.value, undefined, { de: filters.value.de, ate: filters.value.ate }))

const summary = useAsync((signal) => api.get<ResumoVendas>('/api/vendas/resumo', { de: range.value.de, ate: range.value.ate }, signal))
watch(range, () => void summary.run(), { immediate: true })
watch(summary.error, (e) => e && toast.error('Não foi possível carregar o relatório', e.message))

const metric = ref<'faturamento' | 'lucro'>('faturamento')
const metricOptions = [
  { value: 'faturamento' as const, label: 'Faturamento' },
  { value: 'lucro' as const, label: 'Lucro' },
]

const stats = computed<Stat[]>(() => {
  const t = summary.data.value?.totais
  if (!t) return []
  return [
    { label: 'Faturamento', value: t.faturamento },
    { label: 'Lucro', value: t.lucro, tone: t.lucro < 0 ? 'danger' : 'default', note: `Margem de ${formatPercent(t.faturamento > 0 ? (t.lucro / t.faturamento) * 100 : 0)}` },
    { label: 'Ticket médio', value: t.ticketMedio },
    { label: 'Vendas', value: t.quantidade, count: true },
  ]
})
const origens = computed<RankedRow[]>(() => {
  const list = summary.data.value?.porOrigem ?? []
  const total = list.reduce((s, o) => s + o.faturamento, 0)
  return list.map((o) => {
    const share = total > 0 ? (o.faturamento / total) * 100 : 0
    return {
      key: o.origem, label: origemVenda[o.origem]?.label ?? o.origem, value: formatMoney(o.faturamento), share,
      note: `${pluralize(o.quantidade, 'venda', 'vendas')} · lucro ${formatMoney(o.lucro)} · ${formatPercent(share)}`,
    }
  })
})
const noSales = computed(() => !!summary.data.value && summary.data.value.totais.quantidade === 0)
</script>

<template>
  <PageCard title="Relatório de vendas" subtitle="Faturamento, lucro e origem das vendas no período.">
    <div class="mb-6 flex flex-wrap items-end gap-3">
      <UiChips :model-value="preset" :options="periodOptions" label="Período" @update:model-value="(v) => set({ periodo: v })" />
      <template v-if="preset === 'custom'">
        <UiDatePicker label="De" :model-value="range.de" :max="range.ate" @update:model-value="(v) => v && set({ de: v })" />
        <UiDatePicker label="Até" :model-value="range.ate" :min="range.de" @update:model-value="(v) => v && set({ ate: v })" />
      </template>
    </div>

    <StatBlocks :stats="stats" :loading="!summary.data.value" />

    <div class="mt-8 grid gap-8 lg:grid-cols-3">
      <section class="lg:col-span-2" aria-labelledby="day-title">
        <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
          <h2 id="day-title" class="text-lg font-semibold text-ink">Por dia</h2>
          <UiChips v-model="metric" :options="metricOptions" label="Métrica do gráfico" />
        </div>
        <UiSkeleton v-if="!summary.data.value" class="h-56 w-full" />
        <UiEmptyState v-else-if="noSales" title="Sem vendas no período" description="Escolha outro período para ver o movimento." />
        <SalesChart v-else :days="summary.data.value.porDia" :metric="metric" />
      </section>
      <section aria-labelledby="origin-title">
        <h2 id="origin-title" class="mb-4 text-lg font-semibold text-ink">Por origem</h2>
        <UiSkeleton v-if="!summary.data.value" class="h-40 w-full" />
        <RankedList v-else :rows="origens" label="Faturamento por origem" />
      </section>
    </div>
  </PageCard>
</template>
