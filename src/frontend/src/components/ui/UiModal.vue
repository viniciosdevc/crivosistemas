<script setup lang="ts">
import { X } from 'lucide-vue-next'
import { DialogClose, DialogContent, DialogDescription, DialogOverlay, DialogPortal, DialogRoot, DialogTitle } from 'reka-ui'

withDefaults(defineProps<{ title: string; description?: string; size?: 'sm' | 'md' | 'lg' | 'xl'; compact?: boolean }>(), { size: 'md' })
const open = defineModel<boolean>('open', { default: false })
</script>

<template>
  <DialogRoot v-model:open="open">
    <DialogPortal>
      <DialogOverlay class="ui-overlay fixed inset-0 z-50 bg-overlay" />
      <DialogContent
        :class="[
          'ui-dialog flex max-h-[calc(100dvh-2rem)] w-full flex-col sm:w-[calc(100vw-2rem)]',
          'overflow-hidden rounded-[var(--radius-card)] bg-surface shadow-[0_24px_64px_-16px_rgb(0_0_0/0.25)] focus:outline-none',
          { sm: 'sm:max-w-md', md: 'sm:max-w-xl', lg: 'sm:max-w-3xl', xl: 'sm:max-w-5xl' }[size],
        ]"
      >
        <div class="mx-auto mt-2 h-1 w-10 shrink-0 rounded-full bg-line-strong sm:hidden" aria-hidden="true" />
        <div :class="['flex items-start justify-between gap-4 px-6 pt-6', compact ? 'sm:px-6 sm:pt-5' : 'sm:px-10 sm:pt-9']">
          <div class="flex flex-col gap-1">
            <DialogTitle :class="['text-2xl font-semibold tracking-tight text-ink', compact ? 'sm:text-2xl' : 'sm:text-[28px]']">{{ title }}</DialogTitle>
            <DialogDescription v-if="description" class="text-[15px] text-muted">{{ description }}</DialogDescription>
          </div>
          <DialogClose class="-mt-1 -mr-2 rounded-full p-2 text-muted hover:bg-surface-3 hover:text-ink" aria-label="Fechar">
            <X class="size-5" />
          </DialogClose>
        </div>
        <div :class="['min-h-0 flex-1 overflow-y-auto px-6', compact ? 'py-4 sm:px-6' : 'py-6 sm:px-10']"><slot /></div>
        <div v-if="$slots.footer" class="flex flex-wrap items-center justify-end gap-3 px-6 pb-6 sm:px-10 sm:pb-9">
          <slot name="footer" />
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
