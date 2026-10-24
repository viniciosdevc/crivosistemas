<script setup lang="ts">
import { computed, watch } from 'vue'
import RankedList, { type RankedRow } from '@/components/admin/reports/RankedList.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import { formatMoney, formatPercent, pluralize, todayIso } from '@/lib/format'
import { isPeriodPreset, periodOptions, resolvePeriod, type PeriodPreset } from '@/lib/period'
import { toast } from '@/lib/toast'
import type { ResumoFinanceiro, TipoLancamento } from '@/lib/types'
import { useUrlFilters } from '@/lib/urlFilters'
import { useAsync } from '@/lib/useAsync'

const { filters, set } = useUrlFilters({ periodo: '30d', de: '', ate: '' })
const preset = computed<PeriodPreset>(() => (isPeriodPreset(filters.value.periodo) ? filters.value.periodo : '30d'))
const range = computed(() => resolvePeriod(preset.value, todayIso(), { de: filters.value.de, ate: filters.value.ate }))

const resumo = useAsync((signal) => api.get<ResumoFinanceiro>('/api/financeiro/resumo', { de: range.value.de, ate: range.value.ate }, signal))
watch(range, () => void resumo.run(), { immediate: true })
watch(resumo.error, (e) => e && toast.error('Não foi possível carregar o relatório', e.message))

const stats = computed<Stat[]>(() => {
  const r = resumo.data.value
  if (!r) return []
  return [
    { label: 'Entradas', value: r.periodo.entradas, tone: 'success' },
    { label: 'Saídas', value: r.periodo.saidas, tone: 'danger' },
    { label: 'Resultado', value: r.periodo.resultado, tone: r.periodo.resultado >= 0 ? 'success' : 'danger' },
  ]
})

function rowsOf(tipo: TipoLancamento): RankedRow[] {
  const list = (resumo.data.value?.porCategoria ?? []).filter((c) => c.tipo === tipo).sort((a, b) => b.total - a.total)
  const total = list.reduce((s, c) => s + c.total, 0)
  return list.map((c) => ({
    key: c.categoria, label: c.categoria, value: formatMoney(c.total), share: total > 0 ? (c.total / total) * 100 : 0,
    note: `${pluralize(c.quantidade, 'lançamento', 'lançamentos')} · ${formatPercent(total > 0 ? (c.total / total) * 100 : 0)}`,
  }))
}
const entradas = computed(() => rowsOf('entrada'))
const saidas = computed(() => rowsOf('saida'))
</script>

<template>
  <PageCard title="Financeiro por categoria" subtitle="Para onde o dinheiro entra e sai no período (lançamentos confirmados).">
    <div class="mb-6 flex flex-wrap items-end gap-3">
      <UiChips :model-value="preset" :options="periodOptions" label="Período" @update:model-value="(v) => set({ periodo: v })" />
      <template v-if="preset === 'custom'">
        <UiDatePicker label="De" :model-value="filters.de || range.de" :max="range.ate" @update:model-value="(v) => set({ de: v ?? '' })" />
        <UiDatePicker label="Até" :model-value="filters.ate || range.ate" :min="range.de" @update:model-value="(v) => set({ ate: v ?? '' })" />
      </template>
    </div>

    <StatBlocks :stats="stats" :loading="!resumo.data.value" :skeletons="3" />

    <div class="mt-8 grid gap-8 lg:grid-cols-2">
      <section aria-labelledby="in-title">
        <h2 id="in-title" class="mb-3 text-lg font-semibold text-ink">Entradas por categoria</h2>
        <UiSkeleton v-if="!resumo.data.value" class="h-40 w-full" />
        <UiEmptyState v-else-if="!entradas.length" title="Sem entradas" description="Nenhuma entrada confirmada neste período." />
        <RankedList v-else :rows="entradas" tone="success" label="Entradas por categoria" />
      </section>
      <section aria-labelledby="out-title">
        <h2 id="out-title" class="mb-3 text-lg font-semibold text-ink">Saídas por categoria</h2>
        <UiSkeleton v-if="!resumo.data.value" class="h-40 w-full" />
        <UiEmptyState v-else-if="!saidas.length" title="Sem saídas" description="Nenhuma saída confirmada neste período." />
        <RankedList v-else :rows="saidas" tone="danger" label="Saídas por categoria" />
      </section>
    </div>
  </PageCard>
</template>
