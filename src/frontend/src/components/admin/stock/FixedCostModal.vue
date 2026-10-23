<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import { ApiError, api } from '@/lib/api'
import { formatFixed, parseDecimal } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { ResumoEstoque } from '@/lib/types'

/** Administrador: fixa o custo médio do dólar (R$ por US$) ou volta ao calculado. */
const props = defineProps<{ resumo: ResumoEstoque | null }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [] }>()

const valor = ref('')
const loading = ref(false)
const error = ref<string | null>(null)

watch(open, (v) => {
  if (!v) return
  error.value = null
  valor.value = props.resumo?.custoMedioFixo != null ? String(props.resumo.custoMedioFixo).replace('.', ',') : ''
})

const parsed = computed(() => parseDecimal(valor.value))
const ready = computed(() => parsed.value != null && parsed.value > 0)

async function save(value: number | null) {
  loading.value = true
  error.value = null
  try {
    await api.put('/api/estoque/usd/custo-medio-fixo', { valor: value })
    toast.success(value == null ? 'Custo médio calculado' : 'Custo médio fixado')
    open.value = false
    emit('saved')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Não foi possível salvar o custo médio.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" title="Fixar custo médio" description="Usa um valor fixo no lugar da média das compras ao calcular o custo das vendas.">
    <form id="fixed-form" class="flex flex-col gap-5" novalidate @submit.prevent="save(parsed)">
      <UiInput v-model="valor" label="Custo por dólar" prefix="R$" inputmode="decimal" placeholder="0,00" :hint="resumo ? `Calculado pelas compras: R$ ${formatFixed(resumo.custoMedioCalculado, 4)}` : undefined" />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton v-if="resumo?.custoMedioFixo != null" variant="secondary" size="lg" :disabled="loading" @click="save(null)">Voltar ao calculado</UiButton>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="fixed-form" size="lg" :loading="loading" :disabled="!ready">Fixar</UiButton>
    </template>
  </UiModal>
</template>
