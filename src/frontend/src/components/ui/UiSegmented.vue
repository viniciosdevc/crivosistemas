<script setup lang="ts" generic="T extends string">
/** Controle segmentado em pílula: `tabs` (abas com ícone, largura do conteúdo) ou `full` (ocupa a linha toda). */
import type { Component } from 'vue'
withDefaults(defineProps<{ options: { value: T; label: string; icon?: Component }[]; label?: string; variant?: 'tabs' | 'full'; role?: 'tablist' | 'radiogroup' }>(), {
  variant: 'tabs',
  role: 'radiogroup',
})
const model = defineModel<T>({ required: true })
</script>

<template>
  <div
    :class="['items-center gap-1 rounded-full bg-surface-3 p-1', variant === 'full' ? 'grid' : 'inline-flex max-w-full']"
    :style="variant === 'full' ? { gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))` } : undefined"
    :role="role"
    :aria-label="label"
  >
    <button
      v-for="o in options"
      :key="o.value"
      type="button"
      :role="role === 'tablist' ? 'tab' : 'radio'"
      :aria-selected="role === 'tablist' ? model === o.value : undefined"
      :aria-checked="role === 'radiogroup' ? model === o.value : undefined"
      :class="[
        'inline-flex h-9 min-w-0 items-center justify-center gap-1.5 rounded-full px-3.5 text-[13px] font-semibold transition-colors',
        model === o.value ? 'bg-ink text-surface shadow-sm' : 'text-muted hover:text-ink',
      ]"
      @click="model = o.value"
    >
      <component :is="o.icon" v-if="o.icon" class="size-4 shrink-0" />
      <span class="truncate">{{ o.label }}</span>
    </button>
  </div>
</template>
