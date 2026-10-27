<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import { ApiError, api } from '@/lib/api'
import { formatDateTime, formatMoney, formatUsd } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { ResultadoRecalculoEstoque } from '@/lib/types'

/** Administrador: refaz custo e baixa de dólar das vendas desde uma data (FIFO pelos lotes do Gestão). Primeiro mostra a prévia, depois aplica. */
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [] }>()

const desde = ref('2026-10-26')
const previa = ref<ResultadoRecalculoEstoque | null>(null)
const loading = ref(false)
const applying = ref(false)
const error = ref<string | null>(null)

watch(open, (v) => {
  if (!v) return
  previa.value = null
  error.value = null
})
watch(desde, () => { previa.value = null })

const valida = computed(() => /^\d{4}-\d{2}-\d{2}$/.test(desde.value))
const diferenca = computed(() => (previa.value ? previa.value.lucroDepois - previa.value.lucroAntes : 0))

async function calcular() {
  loading.value = true
  error.value = null
  try {
    previa.value = await api.post<ResultadoRecalculoEstoque>('/api/estoque/usd/recalculo/previa', { desde: desde.value })
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Não foi possível calcular a prévia.'
  } finally {
    loading.value = false
  }
}

async function aplicar() {
  applying.value = true
  error.value = null
  try {
    const r = await api.post<ResultadoRecalculoEstoque>('/api/estoque/usd/recalculo/aplicar', { desde: desde.value })
    toast.success('Vendas recalculadas', `${r.vendas} venda(s) refeitas pelos lotes do estoque.`)
    open.value = false
    emit('saved')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Não foi possível aplicar o recálculo.'
  } finally {
    applying.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" size="xl" title="Recalcular vendas pelo estoque" description="Refaz o custo e a baixa de dólar das vendas desde o dia escolhido, na ordem em que aconteceram, do lote mais antigo para o mais novo. Pode repetir sem baixar em dobro.">
    <div class="flex flex-col gap-5">
      <div class="flex flex-wrap items-end gap-3">
        <UiInput v-model="desde" label="A partir do dia" placeholder="AAAA-MM-DD" hint="Vendas antes desse dia não mudam." />
        <UiButton variant="secondary" size="lg" :loading="loading" :disabled="!valida" @click="calcular">Ver prévia</UiButton>
      </div>
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
      <template v-if="previa">
        <p class="text-sm text-ink">
          {{ previa.vendas }} venda(s). Lucro de {{ formatMoney(previa.lucroAntes) }} para {{ formatMoney(previa.lucroDepois) }}
          ({{ diferenca >= 0 ? '+' : '' }}{{ formatMoney(diferenca) }}).
          <span v-if="previa.faltouUsd > 0" class="text-danger">Faltou {{ formatUsd(previa.faltouUsd) }} no estoque para cobrir as vendas.</span>
        </p>
        <div class="overflow-x-auto rounded-2xl border border-line">
          <table class="w-full text-left text-sm">
            <thead class="bg-surface-3 text-muted">
              <tr>
                <th class="px-3 py-2">Data</th><th class="px-3 py-2">Item</th><th class="px-3 py-2 text-right">US$</th>
                <th class="px-3 py-2 text-right">Custo antes</th><th class="px-3 py-2 text-right">Custo depois</th>
                <th class="px-3 py-2 text-right">Lucro antes</th><th class="px-3 py-2 text-right">Lucro depois</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="l in previa.linhas" :key="l.vendaId" class="border-t border-line">
                <td class="px-3 py-2 whitespace-nowrap">{{ formatDateTime(l.dataHora) }}</td>
                <td class="max-w-64 truncate px-3 py-2">{{ l.item }}</td>
                <td class="px-3 py-2 text-right tabular-nums">{{ formatUsd(l.custoUsd) }}</td>
                <td class="px-3 py-2 text-right tabular-nums">{{ formatMoney(l.custoAntes) }}</td>
                <td class="px-3 py-2 text-right tabular-nums">{{ formatMoney(l.custoDepois) }}</td>
                <td class="px-3 py-2 text-right tabular-nums">{{ formatMoney(l.lucroAntes) }}</td>
                <td class="px-3 py-2 text-right font-semibold tabular-nums">{{ formatMoney(l.lucroDepois) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
    </div>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Fechar</UiButton>
      <UiButton size="lg" :loading="applying" :disabled="!previa || previa.vendas === 0" @click="aplicar">Aplicar recálculo</UiButton>
    </template>
  </UiModal>
</template>
