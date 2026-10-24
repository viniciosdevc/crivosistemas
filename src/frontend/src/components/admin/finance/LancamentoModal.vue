<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import UiSelect from '@/components/ui/UiSelect.vue'
import UiSwitch from '@/components/ui/UiSwitch.vue'
import { ApiError, api } from '@/lib/api'
import { parseDecimal, todayIso } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { Lancamento, NovoLancamento, TipoLancamento } from '@/lib/types'

/** Lança uma entrada ou saída manual (o PIX das vendas e as compras de dólar entram sozinhos). */
const props = defineProps<{ categorias: string[]; tipoInicial?: TipoLancamento }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [lancamento: Lancamento] }>()

const NOVA = '__nova__'
const form = reactive({ tipo: 'saida' as TipoLancamento, categoria: '', nova: '', descricao: '', valor: '', data: '', pendente: false })
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})

watch(open, (v) => {
  if (!v) return
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  error.value = null
  Object.assign(form, { tipo: props.tipoInicial ?? 'saida', categoria: '', nova: '', descricao: '', valor: '', data: todayIso(), pendente: false })
})

const tipos = [
  { value: 'entrada' as const, label: 'Entrada' },
  { value: 'saida' as const, label: 'Saída' },
]
const options = computed(() => [
  ...props.categorias.map((c) => ({ value: c, label: c })),
  { value: NOVA, label: '+ Nova categoria…' },
])
const categoria = computed(() => (form.categoria === NOVA ? form.nova.trim() : form.categoria))
const valor = computed(() => parseDecimal(form.valor))
const ready = computed(() => !!categoria.value && valor.value != null && valor.value > 0 && !!form.data)

async function submit() {
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  const body: NovoLancamento = {
    tipo: form.tipo, categoria: categoria.value, descricao: form.descricao.trim(), valorBrl: valor.value ?? 0, data: form.data, pendente: form.pendente,
  }
  try {
    const saved = await api.post<Lancamento>('/api/financeiro/lancamentos', body)
    toast.success(form.pendente ? 'Lançamento pendente registrado' : 'Lançamento registrado')
    open.value = false
    emit('saved', saved)
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) error.value = e.message
    } else {
      error.value = 'Não foi possível salvar o lançamento.'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" title="Novo lançamento" description="Registre uma entrada ou saída em reais. Vendas com PIX e compras de dólar já entram sozinhas.">
    <form id="lancamento-form" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <UiChips v-model="form.tipo" :options="tipos" label="Tipo" />
      <UiSelect v-model="form.categoria" label="Categoria" :options="options" placeholder="Escolha uma categoria" :error="fieldErrors.categoria" />
      <UiInput v-if="form.categoria === NOVA" v-model="form.nova" label="Nome da categoria" maxlength="60" />
      <div class="grid gap-5 sm:grid-cols-2">
        <UiInput v-model="form.valor" label="Valor" prefix="R$" inputmode="decimal" placeholder="0,00" :error="fieldErrors.valorBrl" />
        <UiDatePicker v-model="form.data" label="Data" :error="fieldErrors.data" />
      </div>
      <UiInput v-model="form.descricao" label="Descrição" optional maxlength="300" placeholder="Do que se trata" :error="fieldErrors.descricao" />
      <UiSwitch v-model="form.pendente" label="Ainda não entrou/saiu (pendente)" />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="lancamento-form" size="lg" :loading="loading" :disabled="!ready">Registrar</UiButton>
    </template>
  </UiModal>
</template>
