<script setup lang="ts">
/** Lista ranqueada sem cards: nome, valor e uma barra fina, separados por filetes. */
import { computed } from 'vue'

export interface RankedRow { key: string; label: string; value: string; share: number; note?: string }

const props = defineProps<{ rows: RankedRow[]; tone?: 'brand' | 'success' | 'danger'; label: string }>()
const bar = computed(() => ({ brand: 'bg-brand', success: 'bg-success', danger: 'bg-danger' })[props.tone ?? 'brand'])
</script>

<template>
  <ul class="divide-y divide-line border-y border-line" :aria-label="label">
    <li v-for="r in rows" :key="r.key" class="py-3.5">
      <div class="flex items-baseline justify-between gap-3">
        <span class="min-w-0 truncate font-medium text-ink">{{ r.label }}</span>
        <span class="shrink-0 text-ink tabular">{{ r.value }}</span>
      </div>
      <div class="mt-2 h-1.5 overflow-hidden rounded-full bg-surface-3" role="img" :aria-label="`${Math.round(r.share)}%`">
        <div :class="['h-full rounded-full', bar]" :style="{ width: `${Math.max(2, r.share)}%` }" />
      </div>
      <p v-if="r.note" class="mt-1.5 text-[13px] text-muted tabular">{{ r.note }}</p>
    </li>
  </ul>
</template>
