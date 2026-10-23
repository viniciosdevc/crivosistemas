<script setup lang="ts" generic="T extends string">
/** Underlined tabs used as view filters above tables. */
defineProps<{ tabs: { value: T; label: string; count?: number | null }[] }>()
const model = defineModel<T>({ required: true })
</script>

<template>
  <div class="no-scrollbar -mx-1 overflow-x-auto overflow-y-hidden border-b border-line px-1" role="tablist">
    <div class="flex gap-6">
      <button
        v-for="tab in tabs"
        :key="tab.value"
        type="button"
        role="tab"
        :aria-selected="model === tab.value"
        :class="[
          '-mb-px flex items-center gap-2 border-b-2 pt-1 pb-3 text-[15px] whitespace-nowrap transition-colors',
          model === tab.value ? 'border-sel-ring font-medium text-sel-text' : 'border-transparent text-muted hover:text-ink',
        ]"
        @click="model = tab.value"
      >
        {{ tab.label }}
        <span v-if="tab.count != null" class="rounded-full bg-surface-3 px-2 py-0.5 text-xs text-muted tabular">{{ tab.count }}</span>
      </button>
    </div>
  </div>
</template>
