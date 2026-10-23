<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import { ApiError } from '@/lib/api'
import { useAuthStore } from '@/stores/auth'
import AuthShell from './AuthShell.vue'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const form = reactive({ email: '', password: '' })
const error = ref<string | null>(null)
const loading = ref(false)
const ready = computed(() => form.email.includes('@') && form.password.length > 0)

async function submit() {
  if (!ready.value) return
  loading.value = true
  error.value = null
  try {
    await auth.login(form.email, form.password)
    // Só aceita destino dentro do painel: evita redirecionamento aberto.
    const next = typeof route.query.next === 'string' && route.query.next.startsWith('/painel') ? route.query.next : '/painel'
    router.replace(next)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Não foi possível entrar.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <AuthShell>
    <template #header>
      <h1 class="text-[30px] leading-tight font-semibold tracking-tight text-ink">Boas-vindas de volta</h1>
      <p class="mt-2 text-[15px] leading-relaxed text-text">Entre para acompanhar suas vendas, clientes e regras de preço.</p>
    </template>

    <p v-if="route.query.expired" class="mb-5 rounded-xl bg-warning-soft px-4 py-3 text-sm text-warning">Sua sessão expirou. Entre novamente.</p>

    <form class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <UiInput v-model="form.email" label="E-mail de acesso" type="email" autocomplete="email" placeholder="voce@exemplo.com" required />
      <div>
        <UiInput v-model="form.password" label="Senha" type="password" autocomplete="current-password" placeholder="Sua senha" required />
        <RouterLink to="/esqueci-senha" class="mt-2 inline-block text-sm text-text underline decoration-line-strong underline-offset-4 hover:text-ink">Esqueci minha senha</RouterLink>
      </div>
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
      <UiButton type="submit" size="lg" block :loading="loading" :disabled="!ready" class="mt-1 disabled:!bg-surface-3 disabled:!text-faint disabled:!opacity-100">
        Entrar
      </UiButton>
    </form>

    <p class="mt-8 text-center text-sm text-muted">Não há cadastro público. Peça seu acesso a um administrador.</p>
  </AuthShell>
</template>
