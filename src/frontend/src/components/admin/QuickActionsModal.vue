<script setup lang="ts">
/** Atalhos do topo: Calc NFT, Moedas e Nova venda num só modal. As regras de preço carregam uma vez ao abrir. */
import { RefreshCw, TriangleAlert } from 'lucide-vue-next'
import { computed, reactive, watch } from 'vue'
import NewSaleForm from '@/components/admin/sales/NewSaleForm.vue'
import CalculatorPanel from '@/components/calculator/CalculatorPanel.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiModal from '@/components/ui/UiModal.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import type { RegrasPrecificacao, VendaLista } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

export type QuickTab = 'nft' | 'moedas' | 'venda'

const open = defineModel<boolean>('open', { default: false })
const tab = defineModel<QuickTab>('tab', { default: 'nft' })
const emit = defineEmits<{ created: [venda: VendaLista] }>()

const tabs: { value: QuickTab; label: string }[] = [
  { value: 'nft', label: 'Calc NFT' },
  { value: 'moedas', label: 'Moedas' },
  { value: 'venda', label: 'Nova venda' },
]
const heads: Record<QuickTab, { title: string; description: string }> = {
  nft: { title: 'Calculadora NFT', description: 'Custo e venda sugerida em Câmbios e PIX.' },
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
  <UiModal v-model:open="open" :title="head.title" :description="head.description" :size="tab === 'venda' ? 'xl' : 'lg'" compact>
    <div class="flex flex-col gap-4">
      <UiChips v-model="tab" :options="tabs" label="Ação" compact />

      <template v-if="tab !== 'venda'">
        <UiEmptyState v-if="error && !data" :icon="TriangleAlert" title="Não foi possível carregar as regras" :description="error.message">
          <UiButton variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
        </UiEmptyState>
        <UiSkeleton v-else-if="!data" class="h-56" />
        <CalculatorPanel v-else :mode="tab" :regras="data" />
      </template>

      <NewSaleForm v-if="open && visited.venda" v-show="tab === 'venda'" @created="onCreated" @cancel="open = false" />
    </div>
  </UiModal>
</template>
