<script setup lang="ts">
/** Totais em blocos separados por filete (sem cards): rótulo, número grande e uma nota. */
import MoneyDisplay from '@/components/ui/MoneyDisplay.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'

export interface Stat {
  label: string
  /** Valor em reais, ou uma contagem quando `count` é verdadeiro. */
  value?: number
  /** Texto livre em destaque (ex.: quantidade decimal, data), no lugar do valor. */
  text?: string
  count?: boolean
  note?: string
  tone?: 'default' | 'success' | 'danger'
}

withDefaults(defineProps<{ stats: Stat[]; loading?: boolean; skeletons?: number }>(), { skeletons: 4 })

const toneClass = { default: '', success: '[&,&_span]:!text-success', danger: '[&,&_span]:!text-danger' }
</script>

<template>
  <dl class="grid grid-cols-2 gap-y-6 lg:flex lg:gap-0" aria-live="polite">
    <template v-if="loading">
      <div v-for="i in skeletons" :key="i" :class="['min-w-0 pr-4 lg:flex-1', i > 1 && 'lg:border-l lg:border-line lg:pl-8']">
        <UiSkeleton class="h-4 w-20" /><UiSkeleton class="mt-3 h-9 w-36" /><UiSkeleton class="mt-2 h-3.5 w-24" />
      </div>
    </template>
    <template v-else>
      <div
        v-for="(s, i) in stats"
        :key="s.label"
        :class="['min-w-0 pr-4 lg:flex-1', i % 2 === 1 && 'border-l border-line pl-4 lg:pl-8', i > 0 && 'lg:border-l lg:border-line lg:pl-8']"
      >
        <dt class="text-[15px] text-muted">{{ s.label }}</dt>
        <dd class="mt-1.5">
          <span v-if="s.text != null || s.count" :class="['text-[34px] font-semibold tracking-tight tabular', s.tone === 'danger' ? 'text-danger' : s.tone === 'success' ? 'text-success' : 'text-ink']">{{ s.text ?? s.value }}</span>
          <MoneyDisplay v-else :value="s.value ?? 0" :class="s.tone ? toneClass[s.tone] : ''" />
          <p v-if="s.note" class="mt-0.5 text-sm text-muted">{{ s.note }}</p>
        </dd>
      </div>
    </template>
  </dl>
</template>
