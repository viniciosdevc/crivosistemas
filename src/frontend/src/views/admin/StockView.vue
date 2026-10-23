<script setup lang="ts">
import { computed } from 'vue'
import StockPanel from '@/components/admin/stock/StockPanel.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import type { TipoEstoque } from '@/lib/types'
import { useUrlFilters } from '@/lib/urlFilters'

const { filters, set } = useUrlFilters({ aba: 'usd' })
const tab = computed<TipoEstoque>({
  get: () => (filters.value.aba === 'esmeraldas' ? 'esmeraldas' : 'usd'),
  set: (v) => set({ aba: v }),
})
const tabs = [
  { value: 'usd' as const, label: 'Dólar (USD)' },
  { value: 'esmeraldas' as const, label: 'Esmeraldas' },
]
</script>

<template>
  <PageCard title="Estoque" subtitle="Dólar e esmeraldas em lotes, com custo médio. Entradas por compra, saídas pelas vendas entregues.">
    <UiTabs v-model="tab" :tabs="tabs" class="mb-6" />
    <StockPanel :key="tab" :tipo="tab" />
  </PageCard>
</template>
