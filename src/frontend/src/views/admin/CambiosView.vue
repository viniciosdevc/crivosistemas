<script setup lang="ts">
import { HandCoins, Scale } from 'lucide-vue-next'
import { computed, ref, useTemplateRef, watch } from 'vue'
import CambiosHistory from '@/components/admin/cambios/CambiosHistory.vue'
import SellCambiosModal from '@/components/admin/cambios/SellCambiosModal.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiButton from '@/components/ui/UiButton.vue'
import { api } from '@/lib/api'
import { formatDecimal } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { ResumoCambios } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const resumo = useAsync((signal) => api.get<ResumoCambios>('/api/cambios/resumo', undefined, signal))
void resumo.run()
watch(resumo.error, (e) => e && toast.error('Não foi possível carregar o saldo', e.message))

const stats = computed<Stat[]>(() => {
  const r = resumo.data.value
  if (!r) return []
  return [
    { label: 'Saldo de câmbios', text: formatDecimal(r.saldo) },
    { label: 'Câmbios vendidos', text: formatDecimal(r.totalVendidoQuantidade) },
    { label: 'Recebido em R$', value: r.totalVendidoBrl },
  ]
})

const history = useTemplateRef<InstanceType<typeof CambiosHistory>>('history')
const modo = ref<'venda' | 'saldo'>('venda')
const open = ref(false)
function abrir(m: 'venda' | 'saldo') { modo.value = m; open.value = true }
function refresh() {
  void resumo.run()
  void history.value?.refresh()
}
</script>

<template>
  <PageCard title="Venda de Câmbios" subtitle="O saldo cresce com as vendas pagas em câmbios que você lança aqui. Vender câmbios debita o saldo e entra no financeiro.">
    <div class="mb-6 flex flex-wrap items-center gap-3">
      <UiButton @click="abrir('venda')"><HandCoins class="size-4" /> Vender câmbios</UiButton>
      <UiButton variant="secondary" @click="abrir('saldo')"><Scale class="size-4" /> Definir saldo</UiButton>
    </div>

    <StatBlocks :stats="stats" :loading="!resumo.data.value" :skeletons="3" />

    <h2 class="mb-4 mt-8 text-lg font-semibold text-ink">Histórico</h2>
    <CambiosHistory ref="history" @changed="resumo.run" />
    <SellCambiosModal v-model:open="open" :modo="modo" :saldo="resumo.data.value?.saldo ?? 0" @saved="refresh" />
  </PageCard>
</template>
