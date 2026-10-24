<script setup lang="ts">
import { CircleHelp, RefreshCw, TriangleAlert } from 'lucide-vue-next'
import { ref } from 'vue'
import CalculatorPanel from '@/components/calculator/CalculatorPanel.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import { api } from '@/lib/api'
import type { RegrasPrecificacao } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const { data, loading, error, run } = useAsync((signal) => api.get<RegrasPrecificacao>('/api/precificacao/regras', undefined, signal))
void run()

const tab = ref<'nft' | 'moedas'>('nft')
const tabs = [
  { value: 'nft' as const, label: 'Calc NFT' },
  { value: 'moedas' as const, label: 'Moedas' },
]
const helpOpen = ref(false)
</script>

<template>
  <PageCard title="Calculadora" subtitle="Custo e venda sugerida em Câmbios e PIX, ou conversão de moedas.">
    <template #actions>
      <UiButton variant="ghost" :aria-expanded="helpOpen" @click="helpOpen = !helpOpen"><CircleHelp class="size-4" /> Sobre o custo</UiButton>
    </template>

    <UiEmptyState v-if="error && !data" :icon="TriangleAlert" title="Não foi possível carregar as regras" :description="error.message">
      <UiButton variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
    </UiEmptyState>

    <UiSkeleton v-else-if="loading && !data" class="h-80 max-w-3xl" />

    <div v-else-if="data" class="max-w-3xl">
      <UiTabs v-model="tab" :tabs="tabs" class="mb-4" />
      <CalculatorPanel :mode="tab" :regras="data" :show-help="helpOpen" />
    </div>
  </PageCard>
</template>
