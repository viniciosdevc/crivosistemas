<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import UiTextarea from '@/components/ui/UiTextarea.vue'
import { ApiError, api } from '@/lib/api'
import { formatDecimal, parseDecimal } from '@/lib/format'
import { toast } from '@/lib/toast'

/** Vender câmbios por R$ ou definir o saldo real (o servidor lança a diferença). */
const props = defineProps<{ modo: 'venda' | 'saldo'; saldo: number }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [] }>()

const venda = computed(() => props.modo === 'venda')
const form = reactive({ quantidade: '', valor: '', nickname: '', texto: '' })
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})

watch(open, (v) => {
  if (!v) return
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  error.value = null
  Object.assign(form, { quantidade: venda.value ? '' : String(props.saldo), valor: '', nickname: '', texto: '' })
})

const quantidade = computed(() => parseDecimal(form.quantidade))
const valor = computed(() => parseDecimal(form.valor))
const ready = computed(() => quantidade.value != null && (venda.value ? quantidade.value > 0 && valor.value != null && valor.value > 0 : quantidade.value >= 0))
const aviso = computed(() => (venda.value && quantidade.value != null && quantidade.value > props.saldo ? `Saldo insuficiente (${formatDecimal(props.saldo)}).` : undefined))

async function submit() {
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  try {
    if (venda.value) {
      await api.post('/api/cambios/vendas', {
        quantidade: quantidade.value ?? 0, valorBrl: valor.value ?? 0, nickname: form.nickname.trim() || null, observacao: form.texto.trim() || null,
      })
    } else {
      await api.post('/api/cambios/saldo', { saldo: quantidade.value ?? 0, motivo: form.texto.trim() || null })
    }
    toast.success(venda.value ? 'Venda de câmbios registrada' : 'Saldo atualizado')
    open.value = false
    emit('saved')
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) error.value = e.message
    } else {
      error.value = 'Não foi possível salvar.'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal
    v-model:open="open"
    :title="venda ? 'Vender câmbios' : 'Definir saldo de câmbios'"
    :description="venda ? 'Debita o saldo e lança o valor em R$ como entrada.' : `Saldo no sistema: ${formatDecimal(saldo)}. A diferença fica registrada no histórico.`"
  >
    <form id="cambios-form" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <UiInput v-model="form.quantidade" :label="venda ? 'Quantidade de moedas' : 'Saldo real'" inputmode="decimal" placeholder="0" :hint="aviso" :error="fieldErrors.quantidade || fieldErrors.saldo" />
      <template v-if="venda">
        <UiInput v-model="form.valor" label="Valor (R$)" inputmode="decimal" placeholder="0,00" :error="fieldErrors.valorBrl" />
        <UiInput v-model="form.nickname" label="Nickname do comprador" optional placeholder="Quem comprou" :error="fieldErrors.nickname" />
      </template>
      <UiTextarea v-model="form.texto" :label="venda ? 'Observação' : 'Motivo'" optional :error="fieldErrors.observacao || fieldErrors.motivo" />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="cambios-form" size="lg" :loading="loading" :disabled="!ready">{{ venda ? 'Registrar venda' : 'Definir saldo' }}</UiButton>
    </template>
  </UiModal>
</template>
