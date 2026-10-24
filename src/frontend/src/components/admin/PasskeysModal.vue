<script setup lang="ts">
import { Fingerprint, Trash2 } from 'lucide-vue-next'
import { ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import { ApiError, api } from '@/lib/api'
import { confirm } from '@/lib/confirm'
import { formatDateTime } from '@/lib/format'
import { passkeyErrorMessage, passkeysSupported, registerPasskey, type PasskeyItem } from '@/lib/passkey'
import { toast } from '@/lib/toast'

const open = defineModel<boolean>('open', { default: false })
const items = ref<PasskeyItem[]>([])
const name = ref('')
const loading = ref(false)
const adding = ref(false)
const error = ref<string | null>(null)
const supported = passkeysSupported()

async function load() {
  loading.value = true
  try {
    items.value = await api.get<PasskeyItem[]>('/api/auth/passkeys')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Não foi possível carregar suas chaves.'
  } finally {
    loading.value = false
  }
}

watch(open, (v) => {
  if (!v) return
  name.value = ''
  error.value = null
  load()
})

async function add() {
  adding.value = true
  error.value = null
  try {
    await registerPasskey(name.value.trim())
    toast.success('Chave de acesso adicionada')
    name.value = ''
    await load()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : passkeyErrorMessage(e, 'Não foi possível adicionar a chave.')
  } finally {
    adding.value = false
  }
}

async function remove(item: PasskeyItem) {
  if (!(await confirm({ title: `Remover "${item.name}"?`, description: 'Você ainda poderá entrar com a senha.', confirmLabel: 'Remover', danger: true }))) return
  try {
    await api.delete(`/api/auth/passkeys/${item.id}`)
    toast.success('Chave removida')
    await load()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Não foi possível remover a chave.'
  }
}
</script>

<template>
  <UiModal v-model:open="open" title="Chaves de acesso" description="Entre sem senha, com biometria ou PIN do aparelho." size="sm">
    <div class="flex flex-col gap-5">
      <p v-if="!supported" class="rounded-xl bg-warning-soft px-4 py-3 text-sm text-warning">Este navegador não oferece chaves de acesso.</p>

      <ul v-if="items.length" class="flex flex-col gap-2">
        <li v-for="item in items" :key="item.id" class="flex items-center gap-3 rounded-2xl border border-line px-4 py-3">
          <Fingerprint class="size-5 shrink-0 text-muted" />
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-medium text-ink">{{ item.name }}</p>
            <p class="truncate text-xs text-muted">
              Criada em {{ formatDateTime(item.createdAt) }} · {{ item.lastUsedAt ? `usada em ${formatDateTime(item.lastUsedAt)}` : 'nunca usada' }}
            </p>
          </div>
          <UiButton variant="ghost" size="icon-sm" aria-label="Remover chave" @click="remove(item)"><Trash2 class="size-4" /></UiButton>
        </li>
      </ul>
      <p v-else-if="!loading" class="text-sm text-muted">Você ainda não tem chaves de acesso.</p>

      <form v-if="supported" class="flex flex-col gap-3" novalidate @submit.prevent="add">
        <UiInput v-model="name" label="Nome da nova chave" placeholder="Ex.: Notebook, iPhone" />
        <UiButton type="submit" size="lg" block :loading="adding" :disabled="name.trim().length === 0">Adicionar chave</UiButton>
      </form>

      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </div>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Fechar</UiButton>
    </template>
  </UiModal>
</template>
