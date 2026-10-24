<script setup lang="ts">
import { ChartColumn, RefreshCw, TriangleAlert } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import SalesChart from '@/components/admin/dashboard/SalesChart.vue'
import SaleTable from '@/components/admin/sales/SaleTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import { formatDay, formatMoney, formatPercent, pluralize } from '@/lib/format'
import { origemVenda } from '@/lib/labels'
import { isPeriodPreset, periodOptions, resolvePeriod, type PeriodPreset } from '@/lib/period'
import { toast } from '@/lib/toast'
import type { Paged, ResumoVendas, VendaLista } from '@/lib/types'
import { useUrlFilters } from '@/lib/urlFilters'
import { useAsync } from '@/lib/useAsync'

const { filters, set } = useUrlFilters({ periodo: '30d', de: '', ate: '', metrica: 'faturamento' })
const preset = computed<PeriodPreset>(() => (isPeriodPreset(filters.value.periodo) ? filters.value.periodo : '30d'))
const range = computed(() => resolvePeriod(preset.value, undefined, { de: filters.value.de, ate: filters.value.ate }))
const metric = computed<'faturamento' | 'lucro'>(() => (filters.value.metrica === 'lucro' ? 'lucro' : 'faturamento'))

function setPreset(value: PeriodPreset) {
  // Ao abrir "Personalizado", parte do período que já estava na tela.
  if (value === 'custom') set({ periodo: value, de: range.value.de, ate: range.value.ate })
  else set({ periodo: value, de: '', ate: '' })
}

const summary = useAsync((signal) => api.get<ResumoVendas>('/api/vendas/resumo', { de: range.value.de, ate: range.value.ate }, signal))
const recent = useAsync((signal) => api.get<Paged<VendaLista>>('/api/vendas', { page: 1, pageSize: 8, ordem: 'data', dir: 'desc' }, signal))

watch(range, () => void summary.run(), { immediate: true })
void recent.run()
watch(summary.error, (e) => e && toast.error('Não foi possível carregar o painel', e.message))

const totals = computed(() => summary.data.value?.totais)
const margin = computed(() => (totals.value && totals.value.faturamento > 0 ? (totals.value.lucro / totals.value.faturamento) * 100 : 0))
const stats = computed<Stat[]>(() => {
  const t = totals.value
  if (!t) return []
  return [
    { label: 'Faturamento', value: t.faturamento },
    { label: 'Lucro', value: t.lucro, note: `Margem de ${formatPercent(margin.value)}` },
    { label: 'Ticket médio', value: t.ticketMedio },
    { label: 'Vendas', value: t.quantidade, count: true, note: 'Pagas ou concluídas' },
  ]
})
const origins = computed(() => {
  const list = summary.data.value?.porOrigem ?? []
  const total = list.reduce((s, o) => s + o.faturamento, 0)
  return list.map((o) => ({ ...o, share: total > 0 ? (o.faturamento / total) * 100 : 0 }))
})
const noSales = computed(() => !!summary.data.value && summary.data.value.totais.quantidade === 0)
const customError = computed(() => (preset.value === 'custom' && filters.value.de && filters.value.ate && filters.value.de > filters.value.ate ? 'A data inicial deve ser anterior à final.' : null))

const metricOptions = [
  { value: 'faturamento' as const, label: 'Faturamento' },
  { value: 'lucro' as const, label: 'Lucro' },
]
const metricModel = ref(metric.value)
watch(metric, (m) => (metricModel.value = m))
watch(metricModel, (m) => m !== metric.value && set({ metrica: m === 'faturamento' ? '' : m }))
</script>

<template>
  <PageCard title="Painel" subtitle="Vendas, lucro e origem das vendas no período.">
    <div class="mb-6 flex flex-col gap-4">
      <UiChips :model-value="preset" :options="periodOptions" label="Período" @update:model-value="setPreset" />
      <div v-if="preset === 'custom'" class="grid max-w-xl gap-4 sm:grid-cols-2">
        <UiDatePicker label="De" :model-value="range.de" :max="range.ate" @update:model-value="(v) => v && set({ de: v })" />
        <UiDatePicker label="Até" :model-value="range.ate" :min="range.de" :error="customError" @update:model-value="(v) => v && set({ ate: v })" />
      </div>
      <p class="text-sm text-muted">
        {{ range.de === range.ate ? formatDay(range.de) : `${formatDay(range.de)} a ${formatDay(range.ate)}` }}
      </p>
    </div>

    <UiEmptyState
      v-if="summary.error.value && !summary.data.value"
      :icon="TriangleAlert"
      title="Não foi possível carregar o painel"
      :description="summary.error.value.message"
    >
      <UiButton variant="secondary" @click="summary.run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
    </UiEmptyState>

    <template v-else>
      <StatBlocks :stats="stats" :loading="!summary.data.value" />

      <div class="mt-8 grid gap-4 lg:grid-cols-3 lg:gap-6">
        <section class="rounded-2xl border border-line p-5 lg:col-span-2" aria-labelledby="chart-title">
          <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
            <h2 id="chart-title" class="text-lg font-semibold text-ink">Por dia</h2>
            <UiChips v-model="metricModel" :options="metricOptions" label="Métrica do gráfico" />
          </div>
          <UiSkeleton v-if="!summary.data.value" class="h-56 w-full" />
          <UiEmptyState v-else-if="noSales" :icon="ChartColumn" title="Sem vendas no período" description="Escolha outro período para ver o movimento." />
          <SalesChart v-else :days="summary.data.value.porDia" :metric="metric" />
        </section>

        <section class="rounded-2xl border border-line p-5" aria-labelledby="origin-title">
          <h2 id="origin-title" class="mb-4 text-lg font-semibold text-ink">Por origem</h2>
          <div v-if="!summary.data.value" class="space-y-5"><UiSkeleton v-for="i in 3" :key="i" class="h-12 w-full" /></div>
          <ul v-else class="space-y-5">
            <li v-for="o in origins" :key="o.origem">
              <div class="flex items-baseline justify-between gap-3">
                <span class="font-medium text-ink">{{ origemVenda[o.origem]?.label ?? o.origem }}</span>
                <span class="text-ink tabular">{{ formatMoney(o.faturamento) }}</span>
              </div>
              <div class="mt-2 h-2 overflow-hidden rounded-full bg-surface-3" role="img" :aria-label="`${formatPercent(o.share)} do faturamento`">
                <div class="h-full rounded-full bg-brand" :style="{ width: `${o.share}%` }" />
              </div>
              <p class="mt-1.5 text-[13px] text-muted tabular">
                {{ pluralize(o.quantidade, 'venda', 'vendas') }} · lucro {{ formatMoney(o.lucro) }} · {{ formatPercent(o.share) }}
              </p>
            </li>
          </ul>
        </section>
      </div>

      <section class="mt-6" aria-labelledby="recent-title">
        <div class="mb-3 flex items-center justify-between gap-3">
          <h2 id="recent-title" class="text-lg font-semibold text-ink">Últimas vendas</h2>
          <UiButton variant="ghost" size="sm" to="/painel/vendas">Ver todas</UiButton>
        </div>
        <SaleTable :rows="recent.data.value?.items" :loading="recent.loading.value" :error="recent.error.value?.message" :skeleton-rows="4" compact @retry="recent.run" />
      </section>
    </template>
  </PageCard>
</template>
