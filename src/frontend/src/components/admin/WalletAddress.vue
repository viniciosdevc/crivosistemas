<script setup lang="ts">
import { Check, Copy, ExternalLink } from 'lucide-vue-next'
import { onBeforeUnmount, ref } from 'vue'
import { shortWallet, walletExplorerUrl } from '@/lib/format'
import { toast } from '@/lib/toast'

/** Carteira do cliente: abreviada na lista, completa (com link para o explorer) no detalhe; sempre com botão de copiar. */
const props = withDefaults(defineProps<{ address: string | null | undefined; full?: boolean }>(), { full: false })

const copied = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined
onBeforeUnmount(() => clearTimeout(timer))

async function copy() {
  if (!props.address) return
  try {
    await navigator.clipboard.writeText(props.address)
    copied.value = true
    clearTimeout(timer)
    timer = setTimeout(() => (copied.value = false), 1500)
    toast.success('Carteira copiada')
  } catch {
    toast.error('Não foi possível copiar', 'Selecione o endereço e copie manualmente.')
  }
}
</script>

<template>
  <span v-if="address" class="inline-flex max-w-full items-center gap-1.5">
    <a
      v-if="full"
      :href="walletExplorerUrl(address)"
      target="_blank"
      rel="noopener noreferrer"
      class="inline-flex min-w-0 items-center gap-1 break-all font-mono text-[13px] text-ink hover:underline"
      title="Abrir no explorer da Immutable"
    >
      {{ address }} <ExternalLink class="size-3.5 shrink-0" aria-hidden="true" />
    </a>
    <span v-else class="font-mono text-[13px] text-text" :title="address">{{ shortWallet(address) }}</span>
    <button
      type="button"
      class="inline-flex size-7 shrink-0 items-center justify-center rounded-full text-muted hover:bg-surface-3 hover:text-ink focus-visible:outline-2"
      aria-label="Copiar carteira"
      title="Copiar carteira"
      @click.stop.prevent="copy"
    >
      <Check v-if="copied" class="size-4" aria-hidden="true" />
      <Copy v-else class="size-4" aria-hidden="true" />
    </button>
  </span>
  <span v-else class="text-faint">—</span>
</template>
