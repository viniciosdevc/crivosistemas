<script setup lang="ts">
import { CircleAlert, CircleCheck, Info, X } from 'lucide-vue-next'
import { dismiss, toasts } from '@/lib/toast'

const icons = { success: CircleCheck, error: CircleAlert, info: Info }
</script>

<template>
  <div class="pointer-events-none fixed inset-x-0 bottom-4 z-[60] flex flex-col items-center gap-2 px-4 sm:inset-x-auto sm:right-6 sm:bottom-6 sm:items-end" aria-live="polite">
    <TransitionGroup
      enter-from-class="opacity-0 translate-y-2"
      enter-active-class="transition duration-200"
      leave-to-class="opacity-0"
      leave-active-class="transition duration-150"
    >
      <div
        v-for="t in toasts"
        :key="t.id"
        role="status"
        class="pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-2xl border border-line bg-surface p-4 shadow-[0_16px_40px_-12px_rgb(0_0_0/0.2)]"
      >
        <component :is="icons[t.tone]" :class="['mt-0.5 size-5 shrink-0', { success: 'text-success', error: 'text-danger', info: 'text-info' }[t.tone]]" />
        <div class="min-w-0 flex-1">
          <p class="text-[15px] font-medium text-ink">{{ t.title }}</p>
          <p v-if="t.description" class="mt-0.5 text-sm text-muted">{{ t.description }}</p>
          <button v-if="t.action" type="button" class="mt-2 rounded-full bg-brand px-3.5 py-1.5 text-[13px] font-medium text-brand-ink hover:opacity-90" @click="t.action.onClick(); dismiss(t.id)">
            {{ t.action.label }}
          </button>
        </div>
        <button type="button" class="rounded-full p-1 text-muted hover:text-ink" aria-label="Fechar aviso" @click="dismiss(t.id)">
          <X class="size-4" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>
