<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import UiSelect from '@/components/ui/UiSelect.vue'
import { ApiError, api } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { NftCategoria } from '@/lib/types'

/** Cadastra uma coleção no crivonft com as posições vazias; o preenchimento dos itens continua lá. */
const props = defineProps<{ categorias: NftCategoria[] }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [] }>()

const form = reactive({ nome: '', categoria: '', contrato: '', posicoes: '30' })
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})
const options = computed(() => [{ value: '', label: 'Sem categoria' }, ...props.categorias.map((c) => ({ value: c.categoria, label: c.rotulo }))])
const ready = computed(() => form.nome.trim().length > 0)

watch(open, (v) => {
  if (!v) return
  Object.assign(form, { nome: '', categoria: '', contrato: '', posicoes: '30' })
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
})

async function submit() {
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  try {
    await api.post('/api/nfts/colecoes', {
      nome: form.nome.trim(),
      categoria: form.categoria || null,
      contratoOverride: form.contrato.trim() || null,
      totalPosicoes: Number(form.posicoes) || null,
    })
    toast.success('Coleção cadastrada', form.nome.trim())
    open.value = false
    emit('saved')
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) error.value = e.message
    } else {
      error.value = 'Não foi possível cadastrar a coleção.'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" title="Nova coleção" description="Fica salva no crivonft, com as posições vazias para preencher.">
    <form id="colecao-form" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <UiInput v-model="form.nome" label="Nome" placeholder="Ex.: Pets raros" :error="fieldErrors.nome" />
      <UiSelect v-model="form.categoria" label="Categoria" :options="options" :error="fieldErrors.categoria" />
      <UiInput v-model="form.contrato" label="Contrato (opcional)" placeholder="0x…" hint="Só se a coleção usa um contrato diferente do da categoria." :error="fieldErrors.contratoOverride" />
      <UiInput v-model="form.posicoes" label="Posições" inputmode="numeric" hint="De 1 a 500." :error="fieldErrors.totalPosicoes" />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="colecao-form" size="lg" :loading="loading" :disabled="!ready">Cadastrar</UiButton>
    </template>
  </UiModal>
</template>
