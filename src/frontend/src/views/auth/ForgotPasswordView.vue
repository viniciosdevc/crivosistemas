<script setup lang="ts">
import { MailCheck } from 'lucide-vue-next'
import { ref } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import { ApiError, api } from '@/lib/api'
import AuthShell from './AuthShell.vue'

const email = ref('')
const loading = ref(false)
const sent = ref(false)
const error = ref<string | null>(null)

async function submit() {
  loading.value = true
  error.value = null
  try {
    await api.post('/api/auth/forgot-password', { email: email.value.trim() })
    sent.value = true
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Não foi possível enviar agora.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <AuthShell>
    <template #header>
      <span v-if="sent" class="mb-5 flex size-16 items-center justify-center rounded-full bg-brand-soft text-[#3d7a1c] dark:text-brand"><MailCheck class="size-8" /></span>
      <h1 class="text-[30px] leading-tight font-semibold tracking-tight text-ink">{{ sent ? 'Verifique seu e-mail' : 'Esqueceu a senha?' }}</h1>
      <p class="mt-2 text-[15px] leading-relaxed text-text">
        {{ sent ? 'Se existir uma conta com esse e-mail, enviamos um link para criar uma nova senha. Ele vale por 1 hora.' : 'Informe seu e-mail e enviaremos um link para criar uma nova senha.' }}
      </p>
    </template>

    <form v-if="!sent" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <UiInput v-model="email" label="E-mail de acesso" type="email" autocomplete="email" placeholder="voce@exemplo.com" required />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
      <UiButton type="submit" size="lg" block :loading="loading" :disabled="!email.includes('@')">Enviar link</UiButton>
    </form>
    <UiButton v-else to="/entrar" variant="secondary" size="lg" block>Voltar para o login</UiButton>

    <p v-if="!sent" class="mt-8 text-center text-[15px] text-text">
      Lembrou?
      <RouterLink to="/entrar" class="font-medium text-ink underline decoration-brand decoration-2 underline-offset-4">Entrar</RouterLink>
    </p>
  </AuthShell>
</template>
