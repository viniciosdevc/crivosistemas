<script setup lang="ts">
/** Atalhos do topo: Calc NFT, Moedas e Nova venda num só modal. As regras de preço carregam uma vez ao abrir. */
import { Calculator, CircleHelp, Coins, Plus, RefreshCw, Search, TriangleAlert } from 'lucide-vue-next'
import { PopoverContent, PopoverPortal, PopoverRoot, PopoverTrigger } from 'reka-ui'
import { computed, reactive, watch } from 'vue'
import NewSaleForm from '@/components/admin/sales/NewSaleForm.vue'
import NftSearch from '@/components/calculator/NftSearch.vue'
import CalculatorPanel from '@/components/calculator/CalculatorPanel.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiSegmented from '@/components/ui/UiSegmented.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiModal from '@/components/ui/UiModal.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import { dolarCompraParaCalculo } from '@/lib/calculator'
import { formatMoney } from '@/lib/format'
import type { RegrasPrecificacao, VendaLista } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

export type QuickTab = 'nft' | 'busca' | 'moedas' | 'venda'

const open = defineModel<boolean>('open', { default: false })
const tab = defineModel<QuickTab>('tab', { default: 'nft' })
const emit = defineEmits<{ created: [venda: VendaLista] }>()

const tabs = [
  { value: 'nft' as const, label: 'Calc NFT', icon: Calculator },
  { value: 'busca' as const, label: 'Buscar NFT', icon: Search },
  { value: 'moedas' as const, label: 'Moedas', icon: Coins },
  { value: 'venda' as const, label: 'Nova venda', icon: Plus },
]
const heads: Record<QuickTab, { title: string; description: string }> = {
  nft: { title: 'Calculadora NFT', description: 'Custo e venda sugerida em Câmbios e PIX.' },
  busca: { title: 'Buscar NFT', description: 'Digite o nome e veja foto, preços, lucro e margem.' },
  moedas: { title: 'Conversor de moedas', description: 'Converta entre dólar, real, câmbios e esmeraldas.' },
  venda: { title: 'Nova venda', description: 'Lance uma venda feita fora do site. O custo, o lucro e a margem são recalculados pelo servidor.' },
}
const head = computed(() => heads[tab.value])

const { data, error, run } = useAsync((signal) => api.get<RegrasPrecificacao>('/api/precificacao/regras', undefined, signal))
// O formulário de venda só monta quando visitado, e fica montado (v-show) para não perder o rascunho ao trocar de aba.
const visited = reactive({ venda: false })
watch(
  open,
  (v) => {
    if (v) {
      void run()
      visited.venda = tab.value === 'venda'
    }
  },
  { immediate: true },
)
watch(tab, (t) => {
  if (open.value && t === 'venda') visited.venda = true
})

function onCreated(venda: VendaLista) {
  open.value = false
  emit('created', venda)
}
</script>

<template>
  <UiModal v-model:open="open" :title="head.title" :description="head.description" :size="tab === 'venda' ? 'xl' : tab === 'busca' ? 'md' : 'sm'" compact>
    <template #actions>
      <PopoverRoot v-if="data && tab === 'nft'">
        <PopoverTrigger class="rounded-full p-2 text-muted hover:bg-surface-3 hover:text-ink" aria-label="Sobre o custo usado no cálculo">
          <CircleHelp class="size-5" />
        </PopoverTrigger>
        <PopoverPortal>
          <PopoverContent align="end" :side-offset="6" class="z-[60] w-80 rounded-2xl border border-line bg-surface p-4 text-[13px] leading-relaxed text-muted shadow-[0_12px_32px_-12px_rgb(0_0_0/0.3)]">
            <p class="font-semibold text-ink">Como o custo é calculado</p>
            <p class="mt-1.5">
              O custo é o custo médio do estoque de USD (o mesmo da tela de Estoque); o dólar de Preços e regras ({{ formatMoney(dolarCompraParaCalculo(data.configuracao)) }}) só vale se o estoque estiver vazio. O PIX é sempre o preço da faixa.
            </p>
          </PopoverContent>
        </PopoverPortal>
      </PopoverRoot>
    </template>
    <template #tabs><UiSegmented v-model="tab" :options="tabs" label="Ação" variant="tabs" role="tablist" /></template>

    <div class="flex flex-col gap-4">

      <template v-if="tab !== 'venda'">
        <UiEmptyState v-if="error && !data" :icon="TriangleAlert" title="Não foi possível carregar as regras" :description="error.message">
          <UiButton variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
        </UiEmptyState>
        <UiSkeleton v-else-if="!data" class="h-56" />
        <NftSearch v-else-if="tab === 'busca'" :regras="data" />
        <CalculatorPanel v-else :mode="tab === 'moedas' ? 'moedas' : 'nft'" :regras="data" />
      </template>

      <NewSaleForm v-if="open && visited.venda" v-show="tab === 'venda'" @created="onCreated" @cancel="open = false" />
    </div>
  </UiModal>
</template>
