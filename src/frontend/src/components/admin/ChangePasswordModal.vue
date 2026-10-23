<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import { ApiError, api } from '@/lib/api'
import { toast } from '@/lib/toast'

const open = defineModel<boolean>('open', { default: false })
const form = reactive({ currentPassword: '', newPassword: '', confirmation: '' })
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})

const mismatch = computed(() => form.confirmation.length > 0 && form.confirmation !== form.newPassword)
const ready = computed(() => form.currentPassword.length > 0 && form.newPassword.length >= 8 && form.confirmation === form.newPassword)

watch(open, (v) => {
  if (!v) return
  Object.assign(form, { currentPassword: '', newPassword: '', confirmation: '' })
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  error.value = null
})

async function submit() {
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  try {
    await api.post('/api/auth/change-password', { currentPassword: form.currentPassword, newPassword: form.newPassword })
    toast.success('Senha alterada')
    open.value = false
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) error.value = e.message
    } else {
      error.value = 'Não foi possível alterar a senha.'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" title="Alterar senha" description="Use pelo menos 8 caracteres." size="sm">
    <form id="change-password-form" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <UiInput v-model="form.currentPassword" label="Senha atual" type="password" autocomplete="current-password" :error="fieldErrors.currentPassword" />
      <UiInput v-model="form.newPassword" label="Nova senha" type="password" autocomplete="new-password" :error="fieldErrors.newPassword" />
      <UiInput v-model="form.confirmation" label="Confirmar nova senha" type="password" autocomplete="new-password" :error="mismatch ? 'As senhas não coincidem.' : null" />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="change-password-form" size="lg" :loading="loading" :disabled="!ready">Salvar senha</UiButton>
    </template>
  </UiModal>
</template>
