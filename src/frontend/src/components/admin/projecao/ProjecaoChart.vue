<script setup lang="ts">
/** Linha do acumulado dia a dia em SVG puro: projetado (tracejado) e realizado (cheio, só até hoje). */
import { computed, ref } from 'vue'
import { formatDay, formatMoney } from '@/lib/format'

const props = defineProps<{ dias: { dia: string; projetado: number; realizado: number | null }[]; label: string }>()

const W = 800
const H = 240
const PAD = { l: 8, r: 8, t: 12, b: 8 }
const active = ref<number | null>(null)

const bounds = computed(() => {
  const all = props.dias.flatMap((d) => (d.realizado == null ? [d.projetado] : [d.projetado, d.realizado]))
  const lo = Math.min(0, ...all)
  const hi = Math.max(0, ...all)
  return { lo, hi: hi === lo ? lo + 1 : hi }
})
const x = (i: number) => PAD.l + (props.dias.length > 1 ? (i / (props.dias.length - 1)) * (W - PAD.l - PAD.r) : (W - PAD.l - PAD.r) / 2)
const y = (v: number) => PAD.t + (1 - (v - bounds.value.lo) / (bounds.value.hi - bounds.value.lo)) * (H - PAD.t - PAD.b)
const path = (pick: (d: (typeof props.dias)[number]) => number | null) =>
  props.dias.flatMap((d, i) => { const v = pick(d); return v == null ? [] : [`${i === 0 || pick(props.dias[i - 1]!) == null ? 'M' : 'L'}${x(i).toFixed(1)},${y(v).toFixed(1)}`] }).join(' ')
const projetado = computed(() => path((d) => d.projetado))
const realizado = computed(() => path((d) => d.realizado))
const zeroY = computed(() => y(0))
const ticks = computed(() => [bounds.value.hi, (bounds.value.hi + bounds.value.lo) / 2, bounds.value.lo])
const shown = computed(() => (active.value == null ? null : props.dias[active.value]))

function move(e: PointerEvent) {
  const r = (e.currentTarget as SVGElement).getBoundingClientRect()
  const f = (e.clientX - r.left) / r.width
  active.value = Math.min(props.dias.length - 1, Math.max(0, Math.round(f * (props.dias.length - 1))))
}
</script>

<template>
  <div>
    <div class="mb-2 flex min-h-10 flex-wrap items-center justify-between gap-x-6 gap-y-1 text-sm" aria-live="polite">
      <p class="text-muted">
        <template v-if="shown">
          <span class="font-medium text-ink">{{ formatDay(shown.dia) }}</span>
          · Projetado <span class="tabular text-ink">{{ formatMoney(shown.projetado) }}</span>
          <template v-if="shown.realizado != null"> · Realizado <span class="tabular text-ink">{{ formatMoney(shown.realizado) }}</span></template>
        </template>
        <template v-else>Passe o mouse ou toque no gráfico para ver o dia.</template>
      </p>
      <ul class="flex gap-4 text-muted">
        <li class="flex items-center gap-2"><svg width="22" height="6" aria-hidden="true"><line x1="0" y1="3" x2="22" y2="3" stroke="currentColor" stroke-width="2" stroke-dasharray="4 3" class="text-info" /></svg>Projetado</li>
        <li class="flex items-center gap-2"><svg width="22" height="6" aria-hidden="true"><line x1="0" y1="3" x2="22" y2="3" stroke="currentColor" stroke-width="2.5" class="text-ink" /></svg>Realizado</li>
      </ul>
    </div>
    <div class="flex gap-3">
      <div class="flex h-60 shrink-0 flex-col justify-between text-right text-xs text-faint tabular" aria-hidden="true">
        <span v-for="(t, i) in ticks" :key="i">{{ formatMoney(t) }}</span>
      </div>
      <div class="min-w-0 flex-1">
        <svg
          :viewBox="`0 0 ${W} ${H}`" preserveAspectRatio="none" class="h-60 w-full touch-none overflow-visible" role="img" :aria-label="label"
          @pointermove="move" @pointerdown="move" @pointerleave="active = null"
        >
          <line v-for="(t, i) in ticks" :key="i" x1="0" :x2="W" :y1="y(t)" :y2="y(t)" stroke="currentColor" class="text-line" stroke-dasharray="3 4" vector-effect="non-scaling-stroke" />
          <line v-if="bounds.lo < 0" x1="0" :x2="W" :y1="zeroY" :y2="zeroY" stroke="currentColor" class="text-line-strong" vector-effect="non-scaling-stroke" />
          <path :d="projetado" fill="none" stroke="currentColor" class="text-info" stroke-width="2" stroke-dasharray="6 4" vector-effect="non-scaling-stroke" stroke-linejoin="round" />
          <path :d="realizado" fill="none" stroke="currentColor" class="text-ink" stroke-width="2.5" vector-effect="non-scaling-stroke" stroke-linejoin="round" />
          <line v-if="active != null" :x1="x(active)" :x2="x(active)" y1="0" :y2="H" stroke="currentColor" class="text-line-strong" vector-effect="non-scaling-stroke" />
        </svg>
        <div class="mt-2 flex justify-between text-xs text-faint tabular" aria-hidden="true">
          <span>{{ dias.length ? formatDay(dias[0]!.dia).slice(0, 6) : '' }}</span>
          <span>{{ dias.length ? formatDay(dias[dias.length - 1]!.dia).slice(0, 6) : '' }}</span>
        </div>
      </div>
    </div>
  </div>
</template>
