<script setup lang="ts">
import { computed } from 'vue'
import ItemsPanel from '@/components/admin/stock/ItemsPanel.vue'
import StockPanel from '@/components/admin/stock/StockPanel.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import { useUrlFilters } from '@/lib/urlFilters'

const { filters, set } = useUrlFilters({ aba: 'usd' })
type Aba = 'usd' | 'esmeraldas' | 'itens'
const tab = computed<Aba>({
  get: () => (filters.value.aba === 'esmeraldas' || filters.value.aba === 'itens' ? filters.value.aba : 'usd'),
  set: (v) => set({ aba: v }),
})
const tabs = [
  { value: 'usd' as const, label: 'Dólar (USD)' },
  { value: 'esmeraldas' as const, label: 'Esmeraldas' },
  { value: 'itens' as const, label: 'Itens (NFTs)' },
]
</script>

<template>
  <PageCard title="Estoque" subtitle="Dólar e esmeraldas em lotes, com custo médio, e os itens (NFTs) comprados para revenda.">
    <UiTabs v-model="tab" :tabs="tabs" class="mb-6" />
    <ItemsPanel v-if="tab === 'itens'" />
    <StockPanel v-else :key="tab" :tipo="tab" />
  </PageCard>
</template>
