<script setup lang="ts">
/** Miniatura que amplia a imagem num modal simples (fecha com clique fora ou Esc). Sem imagem, mostra um placeholder neutro. */
import { Image as ImageIcon, X } from 'lucide-vue-next'
import { DialogClose, DialogContent, DialogDescription, DialogOverlay, DialogPortal, DialogRoot, DialogTitle } from 'reka-ui'
import { ref, watch } from 'vue'

const props = withDefaults(defineProps<{ src?: string | null; alt: string; size?: 'sm' | 'lg' }>(), { size: 'sm' })
const open = ref(false)
const failed = ref(false)
watch(() => props.src, () => (failed.value = false))
</script>

<template>
  <button
    v-if="src && !failed"
    type="button"
    :class="['shrink-0 overflow-hidden rounded-xl border border-line bg-surface-2 hover:border-line-strong', size === 'sm' ? 'size-10' : 'size-20 sm:size-24']"
    :aria-label="`Ampliar imagem de ${alt}`"
    @click.stop="open = true"
  >
    <img :src="src" :alt="alt" class="size-full object-cover" loading="lazy" @error="failed = true" />
  </button>
  <span
    v-else
    data-testid="image-placeholder"
    :class="['flex shrink-0 items-center justify-center rounded-xl bg-surface-3 text-faint', size === 'sm' ? 'size-10' : 'size-20 sm:size-24']"
    aria-hidden="true"
  >
    <ImageIcon :class="size === 'sm' ? 'size-4' : 'size-6'" />
  </span>

  <DialogRoot v-if="src" v-model:open="open">
    <DialogPortal>
      <DialogOverlay class="ui-overlay fixed inset-0 z-[60] bg-overlay backdrop-blur-[2px]" />
      <DialogContent
        class="ui-dialog fixed top-1/2 left-1/2 z-[60] flex max-h-[90dvh] max-w-[90vw] -translate-x-1/2 -translate-y-1/2 flex-col items-center gap-3 focus:outline-none"
        @click="open = false"
      >
        <DialogTitle class="sr-only">{{ alt }}</DialogTitle>
        <DialogDescription class="sr-only">Imagem ampliada</DialogDescription>
        <img :src="src" :alt="alt" class="max-h-[80dvh] max-w-full rounded-2xl bg-surface object-contain" @click.stop />
        <p class="rounded-full bg-surface px-4 py-1.5 text-sm text-ink" @click.stop>{{ alt }}</p>
        <DialogClose class="absolute -top-3 -right-3 rounded-full bg-surface p-2 text-muted hover:text-ink" aria-label="Fechar"><X class="size-5" /></DialogClose>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
