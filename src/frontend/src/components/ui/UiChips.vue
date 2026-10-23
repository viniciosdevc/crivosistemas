<script setup lang="ts" generic="T extends string">
/** Pill segmented control (period pickers, quick filters). */
defineProps<{ options: { value: T; label: string }[]; label?: string }>()
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
        'h-10 rounded-full border px-4 text-sm transition-colors',
        model === o.value ? 'border-sel-chip bg-brand-soft font-medium text-sel-text' : 'border-line bg-surface text-text hover:border-line-strong',
      ]"
      @click="model = o.value"
    >
      {{ o.label }}
    </button>
  </div>
</template>
