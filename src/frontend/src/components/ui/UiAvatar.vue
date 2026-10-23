<script setup lang="ts">
import { initials } from '@/lib/format'

withDefaults(defineProps<{ name: string; src?: string | null; size?: 'sm' | 'md' | 'lg' }>(), { size: 'md' })
</script>

<template>
  <span
    :class="[
      'inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full bg-brand-soft font-semibold text-[#3d7a1c] dark:text-brand',
      { sm: 'size-8 text-xs', md: 'size-10 text-sm', lg: 'size-14 text-lg' }[size],
    ]"
  >
    <img v-if="src" :src="src" :alt="name" class="size-full object-cover" loading="lazy" />
    <template v-else>{{ initials(name) }}</template>
  </span>
</template>
