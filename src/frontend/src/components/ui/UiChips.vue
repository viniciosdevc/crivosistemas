<script setup lang="ts" generic="T extends string">
/** Pill segmented control (period pickers, quick filters). */
import type { Component } from 'vue'
defineProps<{ options: { value: T; label: string; icon?: Component }[]; label?: string; compact?: boolean }>()
const model = defineModel<T>({ required: true })
</script>

<template>
  <div class="flex flex-wrap gap-2" role="radiogroup" :aria-label="label">
    <button
      v-for="o in options"
      :key="o.value"
      type="button"
      role="radio"
      :aria-checked="model === o.value"
      :class="[
        'inline-flex items-center gap-1.5 rounded-full border text-sm transition-colors',
        compact ? 'h-8 px-3' : 'h-10 px-4',
        model === o.value ? 'border-sel-chip bg-brand-soft font-medium text-sel-text' : 'border-line bg-surface text-text hover:border-line-strong',
      ]"
      @click="model = o.value"
    >
      <component :is="o.icon" v-if="o.icon" />{{ o.label }}
    </button>
  </div>
</template>
