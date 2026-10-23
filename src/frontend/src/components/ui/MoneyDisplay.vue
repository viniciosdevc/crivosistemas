<script setup lang="ts">
/** Large amount in the reference style: "R$ 1.234" with small cents. */
import { computed } from 'vue'
import { moneyParts } from '@/lib/format'

const props = withDefaults(defineProps<{ value: number; size?: 'md' | 'lg' }>(), { size: 'lg' })
const parts = computed(() => moneyParts(props.value))
</script>

<template>
  <span :class="['inline-flex items-baseline font-semibold tracking-tight text-ink tabular', size === 'lg' ? 'text-[34px]' : 'text-2xl']">
    <span class="text-muted/80 mr-1 font-medium" :class="size === 'lg' ? 'text-[28px]' : 'text-xl'">R$</span>
    {{ parts.int.replace('R$', '').trim() }}<span :class="['ml-0.5 font-medium text-muted', size === 'lg' ? 'text-sm' : 'text-xs']">,{{ parts.cents }}</span>
  </span>
</template>
