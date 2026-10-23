<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{ icon?: Component; title: string; description?: string }>()
</script>

<template>
  <div class="flex flex-col items-center justify-center gap-3 px-6 py-16 text-center">
    <div v-if="icon" class="mb-1 flex size-14 items-center justify-center rounded-2xl bg-surface-3 text-muted">
      <component :is="icon" class="size-6" />
    </div>
    <h3 class="text-lg font-semibold text-ink">{{ title }}</h3>
    <p v-if="description" class="max-w-sm text-[15px] leading-relaxed text-muted">{{ description }}</p>
    <div v-if="$slots.default" class="mt-3 flex flex-wrap justify-center gap-3"><slot /></div>
  </div>
</template>
