<script setup lang="ts">
import { computed } from 'vue'
import { LoaderCircle } from 'lucide-vue-next'
import type { RouteLocationRaw } from 'vue-router'

const props = withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost' | 'danger' | 'subtle'
    size?: 'sm' | 'md' | 'lg' | 'icon' | 'icon-sm'
    loading?: boolean
    disabled?: boolean
    type?: 'button' | 'submit'
    to?: RouteLocationRaw
    href?: string
    block?: boolean
  }>(),
  { variant: 'primary', size: 'md', type: 'button' },
)

const classes = computed(() => [
  'inline-flex items-center justify-center gap-2 rounded-full font-medium whitespace-nowrap select-none transition-colors',
  'disabled:cursor-not-allowed disabled:opacity-50 aria-disabled:pointer-events-none aria-disabled:opacity-50',
  {
    primary: 'bg-brand text-brand-ink hover:bg-brand-hover',
    secondary: 'border border-line-strong bg-surface text-sel-text hover:bg-surface-2',
    ghost: 'text-text hover:bg-surface-3',
    subtle: 'bg-surface-3 text-sel-text hover:bg-line',
    danger: 'bg-danger text-white hover:opacity-90',
  }[props.variant],
  {
    sm: 'h-9 px-4 text-sm',
    md: 'h-11 px-5 text-[15px]',
    lg: 'h-12 px-6 text-[15px]',
    icon: 'size-11 shrink-0',
    'icon-sm': 'size-9 shrink-0',
  }[props.size],
  props.block && 'w-full',
])
</script>

<template>
  <RouterLink v-if="to" :to="to" :class="classes" :aria-disabled="disabled || undefined"><slot /></RouterLink>
  <a v-else-if="href" :href="href" target="_blank" rel="noopener" :class="classes"><slot /></a>
  <button v-else :type="type" :class="classes" :disabled="disabled || loading" :aria-busy="loading || undefined">
    <LoaderCircle v-if="loading" class="size-4 animate-spin" aria-hidden="true" />
    <slot />
  </button>
</template>
