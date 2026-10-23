<script setup lang="ts">
import { ref, watch } from 'vue'
import { initials } from '@/lib/format'

const props = withDefaults(defineProps<{ name: string; src?: string | null; size?: 'sm' | 'md' | 'lg' }>(), { size: 'md' })

// Se a imagem falhar (nick inexistente, fora do ar), volta para as iniciais.
const failed = ref(false)
watch(() => props.src, () => (failed.value = false))
</script>

<template>
  <span
    :class="[
      'inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full bg-brand-soft font-semibold text-[#3d7a1c] dark:text-brand',
      { sm: 'size-8 text-xs', md: 'size-10 text-sm', lg: 'size-14 text-lg' }[size],
    ]"
  >
    <img v-if="src && !failed" :src="src" :alt="name" class="size-full object-contain" loading="lazy" @error="failed = true" />
    <template v-else>{{ initials(name) }}</template>
  </span>
</template>
