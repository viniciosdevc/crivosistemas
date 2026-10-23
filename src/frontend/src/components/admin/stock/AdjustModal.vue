<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import UiTextarea from '@/components/ui/UiTextarea.vue'
import { ApiError, api } from '@/lib/api'
import { formatDecimal, parseDecimal } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { ResumoEstoque, TipoEstoque } from '@/lib/types'

/** Ajuste manual: entrada, saída, ou "definir saldo real" (o servidor lança a diferença). */
const props = defineProps<{ tipo: TipoEstoque; resumo: ResumoEstoque | null }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [] }>()

type Modo = 'entrada' | 'saida' | 'saldo'
const modos: { value: Modo; label: string }[] = [
  { value: 'entrada', label: 'Entrada' },
  { value: 'saida', label: 'Saída' },
  { value: 'saldo', label: 'Definir saldo real' },
]
const usd = computed(() => props.tipo === 'usd')
const form = reactive({ modo: 'entrada' as Modo, valor: '', custoUnitario: '', motivo: '' })
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})

watch(open, (v) => {
  if (!v) return
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  error.value = null
  Object.assign(form, { modo: 'entrada', valor: '', custoUnitario: '', motivo: '' })
})

const valor = computed(() => parseDecimal(form.valor))
const custoUnitario = computed(() => parseDecimal(form.custoUnitario))
const ready = computed(() => valor.value != null && (form.modo === 'saldo' ? valor.value >= 0 : valor.value > 0))
const unidade = computed(() => (usd.value ? 'US$' : 'esmeraldas'))
const custoLabel = computed(() => (usd.value ? 'Custo unitário (R$ por US$)' : 'Custo unitário (US$ por esmeralda)'))
const diferenca = computed(() => (form.modo === 'saldo' && valor.value != null && props.resumo ? valor.value - props.resumo.saldo : null))
const mostraCusto = computed(() => form.modo === 'entrada' || (form.modo === 'saldo' && (diferenca.value ?? 0) > 0))

async function submit() {
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  const custo = mostraCusto.value && custoUnitario.value != null && custoUnitario.value > 0 ? custoUnitario.value : null
  try {
    if (form.modo === 'saldo') await api.post(`/api/estoque/${props.tipo}/saldo`, { saldo: valor.value ?? 0, custoUnitario: custo, motivo: form.motivo.trim() })
    else await api.post(`/api/estoque/${props.tipo}/ajustes`, { direcao: form.modo, quantidade: valor.value ?? 0, custoUnitario: custo, motivo: form.motivo.trim() })
    toast.success(form.modo === 'saldo' ? 'Saldo atualizado' : 'Ajuste registrado')
    open.value = false
    emit('saved')
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) error.value = e.message
    } else {
      error.value = 'Não foi possível registrar o ajuste.'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" title="Ajustar saldo" :description="`Corrige o estoque de ${usd ? 'dólar' : 'esmeraldas'} sem uma compra. Fica registrado nas movimentações.`">
    <form id="adjust-form" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <UiChips v-model="form.modo" :options="modos" label="Tipo de ajuste" />
      <UiInput
        v-model="form.valor"
        :label="form.modo === 'saldo' ? `Saldo real (${unidade})` : `Quantidade (${unidade})`"
        inputmode="decimal"
        placeholder="0"
        :hint="form.modo === 'saldo' && resumo ? `Saldo no sistema: ${formatDecimal(resumo.saldo)}` : undefined"
        :error="fieldErrors.quantidade || fieldErrors.saldo"
      />
      <p v-if="diferenca != null && diferenca !== 0" class="-mt-2 text-[13px] text-muted">
        Vai lançar {{ diferenca > 0 ? 'uma entrada' : 'uma saída' }} de {{ formatDecimal(Math.abs(diferenca)) }}.
      </p>
      <UiInput
        v-if="mostraCusto"
        v-model="form.custoUnitario"
        :label="custoLabel"
        optional
        inputmode="decimal"
        placeholder="Vazio usa o custo médio"
        :error="fieldErrors.custoUnitario"
      />
      <UiTextarea v-model="form.motivo" label="Motivo" optional placeholder="Por que o saldo está sendo ajustado" :error="fieldErrors.motivo" />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="adjust-form" size="lg" :loading="loading" :disabled="!ready">{{ form.modo === 'saldo' ? 'Definir saldo' : 'Registrar ajuste' }}</UiButton>
    </template>
  </UiModal>
</template>
