<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import { ApiError, api } from '@/lib/api'
import { formatMoney, parseDecimal, todayIso } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { ItemEstoque } from '@/lib/types'

/** Dá baixa no item com o valor total recebido; mostra o lucro. */
const props = defineProps<{ item: ItemEstoque | null }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [] }>()

const form = reactive({ data: '', valor: '' })
const loading = ref(false)
const error = ref<string | null>(null)

watch(open, (v) => {
  if (!v) return
  error.value = null
  const p = props.item?.precoMercadoBrl
  Object.assign(form, { data: todayIso(), valor: p != null ? String(p * props.item!.quantidade).replace('.', ',') : '' })
})

const valor = computed(() => parseDecimal(form.valor))
const lucro = computed(() => (valor.value != null && props.item ? valor.value - props.item.custoTotal : null))

async function submit() {
  if (!props.item) return
  loading.value = true
  error.value = null
  try {
    await api.post(`/api/estoque/itens/${props.item.id}/vender`, { valorVenda: valor.value ?? 0, data: form.data })
    toast.success('Venda registrada')
    open.value = false
    emit('saved')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Não foi possível registrar a venda.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" title="Vender item" :description="item ? `${item.nome} · ${item.quantidade} un. · custo ${formatMoney(item.custoTotal)}` : undefined">
    <form id="sell-item-form" class="flex flex-col gap-4" novalidate @submit.prevent="submit">
      <UiInput v-model="form.valor" label="Valor recebido (total)" prefix="R$" inputmode="decimal" placeholder="0,00" />
      <UiDatePicker v-model="form.data" label="Data da venda" :max="todayIso()" />
      <p v-if="lucro != null" class="text-sm text-muted">
        Lucro <span :class="['font-semibold tabular', lucro >= 0 ? 'text-success' : 'text-danger']">{{ formatMoney(lucro) }}</span>
      </p>
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="sell-item-form" size="lg" :loading="loading" :disabled="valor == null || valor < 0 || !form.data">Registrar venda</UiButton>
    </template>
  </UiModal>
</template>
