<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import { ApiError, api } from '@/lib/api'
import { toast } from '@/lib/toast'
import AuthShell from './AuthShell.vue'

const route = useRoute()
const router = useRouter()
const token = typeof route.query.token === 'string' ? route.query.token : ''
const password = ref('')
const confirmation = ref('')
const loading = ref(false)
const error = ref<string | null>(null)
const mismatch = computed(() => confirmation.value.length > 0 && confirmation.value !== password.value)
const ready = computed(() => password.value.length >= 8 && confirmation.value === password.value)

async function submit() {
  loading.value = true
  error.value = null
  try {
    await api.post('/api/auth/reset-password', { token, password: password.value })
    toast.success('Senha atualizada', 'Entre com a nova senha.')
    router.replace('/entrar')
  } catch (e) {
    error.value = e instanceof ApiError ? (e.fields.password ?? e.message) : 'Não foi possível redefinir a senha.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <AuthShell>
    <template #header>
      <h1 class="text-[30px] leading-tight font-semibold tracking-tight text-ink">Crie uma nova senha</h1>
      <p class="mt-2 text-[15px] leading-relaxed text-text">Use pelo menos 8 caracteres.</p>
    </template>

    <div v-if="!token" class="flex flex-col gap-4">
      <p class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">Link inválido. Peça um novo para redefinir sua senha.</p>
      <UiButton to="/esqueci-senha" size="lg" block>Pedir novo link</UiButton>
    </div>
    <form v-else class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <UiInput v-model="password" label="Nova senha" type="password" autocomplete="new-password" placeholder="Mínimo de 8 caracteres" required />
      <UiInput v-model="confirmation" label="Confirmar senha" type="password" autocomplete="new-password" placeholder="Repita a senha" :error="mismatch ? 'As senhas não coincidem.' : null" required />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">
        {{ error }} <RouterLink to="/esqueci-senha" class="font-medium underline underline-offset-4">Pedir novo link</RouterLink>
      </p>
      <UiButton type="submit" size="lg" block :loading="loading" :disabled="!ready" class="disabled:!bg-surface-3 disabled:!text-faint disabled:!opacity-100">Salvar nova senha</UiButton>
    </form>
  </AuthShell>
</template>
