<script setup lang="ts">
import { Pencil, Plus } from 'lucide-vue-next'
import { nextTick, ref } from 'vue'
import WalletAddress from '@/components/admin/WalletAddress.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import { atualizarCliente } from '@/lib/clientes'
import { toast } from '@/lib/toast'
import type { ClienteLista } from '@/lib/types'

/**
 * Carteira do cliente com edição inline. A carteira é um texto livre cadastrado no crivonft: copiar e o link do explorer
 * só aparecem quando é um endereço válido; senão o texto vem com o selo "formato inválido". Quando a loja tem outra
 * carteira vinculada, ela aparece numa segunda linha.
 */
const props = withDefaults(
  defineProps<{ cliente: Pick<ClienteLista, 'id' | 'nickname' | 'carteira' | 'carteiraValida' | 'carteiraLoja'>; full?: boolean }>(),
  { full: false },
)
const emit = defineEmits<{ changed: [cliente: ClienteLista] }>()

const editando = ref(false)
const valor = ref('')
const salvando = ref(false)
const campo = ref<HTMLInputElement | null>(null)

async function editar() {
  valor.value = props.cliente.carteira ?? ''
  editando.value = true
  await nextTick()
  campo.value?.focus()
}

async function salvar(novo: string | null) {
  if (salvando.value) return
  salvando.value = true
  try {
    const salvo = await atualizarCliente(props.cliente.id, { carteira: novo })
    editando.value = false
    emit('changed', salvo)
    toast.success(salvo.carteira ? 'Carteira salva' : 'Carteira removida')
  } catch (e) {
    toast.error('Não foi possível salvar a carteira', e instanceof Error ? e.message : undefined)
  } finally {
    salvando.value = false
  }
}
</script>

<template>
  <div class="min-w-0" @click.stop>
    <form v-if="editando" class="flex flex-wrap items-center gap-2" @submit.prevent="salvar(valor.trim() || null)" @keydown.esc="editando = false">
      <input
        ref="campo"
        v-model="valor"
        type="text"
        maxlength="120"
        class="h-10 min-w-0 flex-1 rounded-xl border border-line bg-surface px-3 font-mono text-[13px] text-ink focus-visible:outline-2"
        :aria-label="`Carteira de ${cliente.nickname}`"
        placeholder="0x… ou texto livre"
      />
      <UiButton type="submit" size="sm" :loading="salvando">Salvar</UiButton>
      <UiButton v-if="cliente.carteira" type="button" size="sm" variant="secondary" :disabled="salvando" @click="salvar(null)">Limpar</UiButton>
      <UiButton type="button" size="sm" variant="ghost" :disabled="salvando" @click="editando = false">Cancelar</UiButton>
    </form>

    <div v-else class="flex min-w-0 flex-wrap items-center gap-1.5">
      <template v-if="cliente.carteira">
        <WalletAddress v-if="cliente.carteiraValida" :address="cliente.carteira" :full="full" />
        <span v-else class="inline-flex min-w-0 items-center gap-2">
          <span class="truncate text-[13px] text-text" :title="cliente.carteira">{{ cliente.carteira }}</span>
          <UiBadge tone="warning">formato inválido</UiBadge>
        </span>
        <button
          type="button"
          class="inline-flex size-7 shrink-0 items-center justify-center rounded-full text-muted hover:bg-surface-3 hover:text-ink focus-visible:outline-2"
          aria-label="Editar carteira"
          title="Editar carteira"
          @click.stop.prevent="editar"
        >
          <Pencil class="size-4" aria-hidden="true" />
        </button>
      </template>
      <button
        v-else
        type="button"
        class="inline-flex items-center gap-1 rounded-full px-2 py-1 text-sm text-muted hover:bg-surface-3 hover:text-ink focus-visible:outline-2"
        @click.stop.prevent="editar"
      >
        <Plus class="size-4" aria-hidden="true" /> Adicionar carteira
      </button>
    </div>

    <p v-if="!editando && cliente.carteiraLoja" class="mt-1 flex flex-wrap items-center gap-1.5 text-[13px] text-muted">
      Carteira vinculada na loja <WalletAddress :address="cliente.carteiraLoja" :full="full" />
    </p>
  </div>
</template>
