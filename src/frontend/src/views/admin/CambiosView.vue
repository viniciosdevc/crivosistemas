<script setup lang="ts">
import { HandCoins, Scale } from 'lucide-vue-next'
import { ref, useTemplateRef, watch } from 'vue'
import CambiosHistory from '@/components/admin/cambios/CambiosHistory.vue'
import SellCambiosModal from '@/components/admin/cambios/SellCambiosModal.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import { formatDecimal, formatMoney } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { ResumoCambios } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const resumo = useAsync((signal) => api.get<ResumoCambios>('/api/cambios/resumo', undefined, signal))
void resumo.run()
watch(resumo.error, (e) => e && toast.error('Não foi possível carregar o saldo', e.message))

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

    <div v-if="!resumo.data.value" class="grid gap-4 sm:grid-cols-3"><UiSkeleton v-for="i in 3" :key="i" class="h-28" /></div>
    <dl v-else class="grid gap-4 sm:grid-cols-3">
      <div class="rounded-2xl border border-line p-5">
        <dt class="text-sm text-muted">Saldo de câmbios</dt>
        <dd class="mt-1 text-2xl font-semibold tracking-tight text-ink tabular">{{ formatDecimal(resumo.data.value.saldo) }}</dd>
      </div>
      <div class="rounded-2xl border border-line p-5">
        <dt class="text-sm text-muted">Câmbios vendidos</dt>
        <dd class="mt-1 text-2xl font-semibold tracking-tight text-ink tabular">{{ formatDecimal(resumo.data.value.totalVendidoQuantidade) }}</dd>
      </div>
      <div class="rounded-2xl border border-line p-5">
        <dt class="text-sm text-muted">Recebido em R$</dt>
        <dd class="mt-1 text-2xl font-semibold tracking-tight text-ink tabular">{{ formatMoney(resumo.data.value.totalVendidoBrl) }}</dd>
      </div>
    </dl>

    <h2 class="mb-4 mt-8 text-lg font-semibold text-ink">Histórico</h2>
    <CambiosHistory ref="history" @changed="resumo.run" />
    <SellCambiosModal v-model:open="open" :modo="modo" :saldo="resumo.data.value?.saldo ?? 0" @saved="refresh" />
  </PageCard>
</template>
