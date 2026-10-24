<script setup lang="ts">
import { Star } from 'lucide-vue-next'
import { ref, watch } from 'vue'
import { atualizarCliente } from '@/lib/clientes'
import { toast } from '@/lib/toast'
import type { ClienteLista } from '@/lib/types'

/** Estrela de favorito: muda na hora (otimista) e volta atrás, com aviso, se o servidor recusar. */
const props = defineProps<{ id: string; favorito: boolean; nickname?: string }>()
const emit = defineEmits<{ changed: [cliente: ClienteLista] }>()

const ligado = ref(props.favorito)
const salvando = ref(false)
watch(() => props.favorito, (v) => (ligado.value = v))

async function alternar() {
  if (salvando.value) return
  const anterior = ligado.value
  const proximo = !anterior
  ligado.value = proximo
  salvando.value = true
  try {
    const salvo = await atualizarCliente(props.id, { favorito: proximo })
    ligado.value = salvo.favorito
    emit('changed', salvo)
  } catch (e) {
    ligado.value = anterior
    toast.error('Não foi possível atualizar o favorito', e instanceof Error ? e.message : undefined)
  } finally {
    salvando.value = false
  }
}
</script>

<template>
  <button
    type="button"
    class="inline-flex size-9 shrink-0 items-center justify-center rounded-full text-muted hover:bg-surface-3 hover:text-ink focus-visible:outline-2 disabled:opacity-60"
    :aria-pressed="ligado"
    :aria-label="ligado ? `Remover ${nickname ?? 'cliente'} dos favoritos` : `Favoritar ${nickname ?? 'cliente'}`"
    :title="ligado ? 'Remover dos favoritos' : 'Favoritar'"
    :disabled="salvando"
    @click.stop.prevent="alternar"
  >
    <Star class="size-5" :class="ligado ? 'fill-warning text-warning' : ''" aria-hidden="true" />
  </button>
</template>
