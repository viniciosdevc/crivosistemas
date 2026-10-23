<script setup lang="ts">
/** Barras por dia em CSS/HTML puro (sem biblioteca): altura proporcional ao maior valor do período. */
import { computed, ref } from 'vue'
import { formatDay, formatMoney, formatInt } from '@/lib/format'
import type { ResumoDia } from '@/lib/types'

const props = defineProps<{ days: ResumoDia[]; metric: 'faturamento' | 'lucro' }>()

const active = ref<number | null>(null)
const values = computed(() => props.days.map((d) => Math.max(0, props.metric === 'lucro' ? d.lucro : d.faturamento)))
const max = computed(() => Math.max(0, ...values.value))
const ticks = computed(() => [1, 0.5, 0].map((f) => ({ f, label: formatMoney(max.value * f) })))
const shown = computed(() => (active.value == null ? null : props.days[active.value]))
const labelIdx = computed(() => {
  const n = props.days.length
  if (n <= 1) return [0]
  const step = Math.max(1, Math.ceil(n / 6))
  const out: number[] = []
  for (let i = 0; i < n; i += step) out.push(i)
  return out
})
const summary = computed(() => `Gráfico de ${props.metric === 'lucro' ? 'lucro' : 'faturamento'} por dia, ${props.days.length} dias`)
</script>

<template>
  <div>
    <div class="mb-3 min-h-10 text-sm text-muted" aria-live="polite">
      <template v-if="shown">
        <span class="font-medium text-ink">{{ formatDay(shown.dia) }}</span>
        · Faturamento {{ formatMoney(shown.faturamento) }} · Lucro {{ formatMoney(shown.lucro) }} · {{ formatInt(shown.quantidade) }}
        {{ shown.quantidade === 1 ? 'venda' : 'vendas' }}
      </template>
      <template v-else>Passe o mouse ou toque em uma barra para ver o dia.</template>
    </div>
    <div class="flex gap-3">
      <div class="flex h-48 shrink-0 flex-col justify-between text-right text-xs text-faint tabular" aria-hidden="true">
        <span v-for="t in ticks" :key="t.f">{{ t.label }}</span>
      </div>
      <div class="relative min-w-0 flex-1">
        <div class="pointer-events-none absolute inset-0 flex flex-col justify-between" aria-hidden="true">
          <div v-for="t in ticks" :key="t.f" class="border-t border-dashed border-line" />
        </div>
        <div
          class="relative flex h-48 items-end gap-px sm:gap-0.5"
          role="img"
          :aria-label="summary"
          @mouseleave="active = null"
        >
          <button
            v-for="(d, i) in days"
            :key="d.dia"
            type="button"
            class="group flex h-full min-w-0 flex-1 items-end focus:outline-none"
            :title="`${formatDay(d.dia)}: ${formatMoney(metric === 'lucro' ? d.lucro : d.faturamento)}`"
            :aria-label="`${formatDay(d.dia)}: ${formatMoney(metric === 'lucro' ? d.lucro : d.faturamento)}`"
            @mouseenter="active = i"
            @focus="active = i"
            @click="active = i"
          >
            <span
              :class="['block w-full rounded-t-sm transition-colors', active === i ? 'bg-brand-hover' : 'bg-brand group-hover:bg-brand-hover']"
              :style="{ height: max > 0 ? `${Math.max((values[i]! / max) * 100, values[i]! > 0 ? 2 : 0)}%` : '0%' }"
            />
          </button>
        </div>
        <div class="relative mt-2 h-4 text-xs text-faint" aria-hidden="true">
          <span
            v-for="i in labelIdx"
            :key="i"
            class="absolute top-0 whitespace-nowrap tabular"
            :style="{ left: `${days.length > 1 ? ((i + 0.5) / days.length) * 100 : 50}%`, transform: 'translateX(-50%)' }"
          >{{ formatDay(days[i]!.dia).slice(0, 6) }}</span>
        </div>
      </div>
    </div>
  </div>
</template>
