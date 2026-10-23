<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import UiTextarea from '@/components/ui/UiTextarea.vue'
import { ApiError, api } from '@/lib/api'
import { formatFixed, parseDecimal, todayIso } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { CompraEstoque, LoteEstoque, TipoEstoque } from '@/lib/types'

/** Registra uma compra de dólar (R$ depositados → US$ recebidos) ou de esmeraldas (US$ pagos → esmeraldas), ou corrige uma ainda intocada. */
const props = defineProps<{ tipo: TipoEstoque; lote?: LoteEstoque | null }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [lote: LoteEstoque] }>()

const usd = computed(() => props.tipo === 'usd')
const editing = computed(() => !!props.lote)
const form = reactive({ data: '', quantidade: '', custoTotal: '', observacao: '' })
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})

const toStr = (n: number) => String(n).replace('.', ',')

watch(open, (v) => {
  if (!v) return
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  error.value = null
  const l = props.lote
  Object.assign(form, l
    ? { data: l.dataCompra, quantidade: toStr(l.quantidade), custoTotal: toStr(l.custoTotal), observacao: l.observacao ?? '' }
    : { data: todayIso(), quantidade: '', custoTotal: '', observacao: '' })
})

const quantidade = computed(() => parseDecimal(form.quantidade) ?? 0)
const custoTotal = computed(() => parseDecimal(form.custoTotal))
const unitario = computed(() => (quantidade.value > 0 && custoTotal.value != null ? custoTotal.value / quantidade.value : null))
const ready = computed(() => !!form.data && quantidade.value > 0 && custoTotal.value != null && custoTotal.value >= 0)

async function submit() {
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  const body: CompraEstoque = { data: form.data, quantidade: quantidade.value, custoTotal: custoTotal.value ?? 0, observacao: form.observacao.trim() }
  try {
    const saved = props.lote
      ? await api.put<LoteEstoque>(`/api/estoque/${props.tipo}/compras/${props.lote.id}`, body)
      : await api.post<LoteEstoque>(`/api/estoque/${props.tipo}/compras`, body)
    toast.success(editing.value ? 'Compra corrigida' : 'Compra registrada')
    open.value = false
    emit('saved', saved)
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) error.value = e.message
    } else {
      error.value = 'Não foi possível salvar a compra.'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal
    v-model:open="open"
    :title="editing ? 'Corrigir compra' : 'Registrar compra'"
    :description="usd ? 'Dólares comprados com reais. O custo por dólar entra na média do estoque.' : 'Esmeraldas compradas com dólar. O custo por esmeralda entra na média do estoque.'"
  >
    <form id="purchase-form" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <UiDatePicker v-model="form.data" label="Data da compra" :max="todayIso()" :error="fieldErrors.data" />
      <UiInput
        v-model="form.quantidade"
        :label="usd ? 'US$ recebidos' : 'Esmeraldas compradas'"
        :prefix="usd ? 'US$' : undefined"
        inputmode="decimal"
        placeholder="0"
        :error="fieldErrors.quantidade"
      />
      <UiInput
        v-model="form.custoTotal"
        :label="usd ? 'R$ depositados' : 'US$ pagos'"
        :prefix="usd ? 'R$' : 'US$'"
        inputmode="decimal"
        placeholder="0,00"
        :error="fieldErrors.custoTotal"
      />
      <div v-if="unitario != null" class="rounded-2xl bg-surface-2 px-4 py-3.5">
        <p class="text-[13px] text-muted">{{ usd ? 'Custo por dólar' : 'Custo por esmeralda' }}</p>
        <p class="mt-1 text-xl font-semibold text-ink tabular">{{ usd ? `R$ ${formatFixed(unitario, 4)} / US$` : `US$ ${formatFixed(unitario, 6)} / esmeralda` }}</p>
      </div>
      <UiTextarea v-model="form.observacao" label="Observação" optional placeholder="Onde comprou, para quem, etc." :error="fieldErrors.observacao" />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="purchase-form" size="lg" :loading="loading" :disabled="!ready">{{ editing ? 'Salvar' : 'Registrar compra' }}</UiButton>
    </template>
  </UiModal>
</template>
