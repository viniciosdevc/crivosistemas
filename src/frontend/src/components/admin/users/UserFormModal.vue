<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import UiSelect from '@/components/ui/UiSelect.vue'
import { ApiError, api } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { AppUser, Role } from '@/lib/types'

/** Cria um usuário (sem `user`) ou edita nome e papel de um existente. O e-mail e a senha não mudam por aqui. */
const props = defineProps<{ user?: AppUser | null }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [user: AppUser] }>()

const roleOptions: { value: Role; label: string }[] = [
  { value: 'operator', label: 'Operador (vendas e clientes)' },
  { value: 'admin', label: 'Administrador (acesso total e usuários)' },
]
const form = reactive({ name: '', email: '', password: '', role: 'operator' as Role })
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})
const editing = computed(() => !!props.user)

const ready = computed(() =>
  form.name.trim().length >= 2 && (editing.value || (form.email.includes('@') && form.password.length >= 8)),
)

watch(open, (v) => {
  if (!v) return
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  error.value = null
  Object.assign(form, props.user
    ? { name: props.user.name, email: props.user.email, password: '', role: props.user.role }
    : { name: '', email: '', password: '', role: 'operator' })
})

async function submit() {
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  try {
    const saved = props.user
      ? await api.put<AppUser>(`/api/users/${props.user.id}`, { name: form.name.trim(), role: form.role })
      : await api.post<AppUser>('/api/users', { name: form.name.trim(), email: form.email.trim(), password: form.password, role: form.role })
    toast.success(props.user ? 'Usuário atualizado' : 'Usuário criado', saved.email)
    open.value = false
    emit('saved', saved)
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) error.value = e.message
    } else {
      error.value = 'Não foi possível salvar o usuário.'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" :title="editing ? 'Editar usuário' : 'Novo usuário'" :description="editing ? user?.email : 'A pessoa entra com o e-mail e a senha definidos aqui e pode trocá-la depois.'">
    <form id="user-form" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <UiInput v-model="form.name" label="Nome" autocomplete="off" placeholder="Nome completo" :error="fieldErrors.name" />
      <UiInput v-if="!editing" v-model="form.email" label="E-mail" type="email" autocomplete="off" placeholder="nome@exemplo.com" :error="fieldErrors.email" />
      <UiInput v-if="!editing" v-model="form.password" label="Senha inicial" type="password" autocomplete="new-password" placeholder="Mínimo de 8 caracteres" :error="fieldErrors.password" />
      <UiSelect v-model="form.role" label="Papel" :options="roleOptions" :error="fieldErrors.role" />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="user-form" size="lg" :loading="loading" :disabled="!ready">{{ editing ? 'Salvar' : 'Criar usuário' }}</UiButton>
    </template>
  </UiModal>
</template>
