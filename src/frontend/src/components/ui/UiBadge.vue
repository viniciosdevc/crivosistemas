<script setup lang="ts">
import type { Component } from 'vue'
import type { Tone } from '@/lib/labels'

withDefaults(defineProps<{ tone?: Tone; icon?: Component }>(), { tone: 'neutral' })
</script>

<template>
  <span
    :class="[
      'inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-[13px] leading-none font-medium whitespace-nowrap',
      {
        neutral: 'bg-surface-3 text-text',
        success: 'bg-success-soft text-success',
        warning: 'bg-warning-soft text-warning',
        danger: 'bg-danger-soft text-danger',
        info: 'bg-info-soft text-info',
        brand: 'bg-brand-soft text-sel-badge dark:text-brand',
      }[tone],
    ]"
  >
    <component :is="icon" v-if="icon" class="size-3.5" stroke-width="2.25" />
    <slot />
  </span>
</template>
