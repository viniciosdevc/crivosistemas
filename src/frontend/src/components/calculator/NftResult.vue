<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { calculateFromUsd } from '@/lib/calculator'
import { formatDecimal, formatInt, formatMoney, formatPercent, parseDecimal } from '@/lib/format'
import type { ConfigCalculo, FaixaPix } from '@/lib/types'
import EditableValue from './EditableValue.vue'

const props = defineProps<{ custoUsd: number; config: ConfigCalculo; faixas: FaixaPix[] }>()

const r = computed(() => calculateFromUsd(props.custoUsd, props.config, props.faixas))

// A simulação manual de preço volta ao sugerido quando o custo muda.
const rawPix = ref('')
const rawCambios = ref('')
watch(() => props.custoUsd, () => { rawPix.value = ''; rawCambios.value = '' })

const simPix = computed(() => (rawPix.value ? (parseDecimal(rawPix.value) ?? r.value.precoVendaPix) : r.value.precoVendaPix))
const simCambios = computed(() => (rawCambios.value ? Math.round(parseDecimal(rawCambios.value) ?? r.value.precoVendaCambios) : r.value.precoVendaCambios))

const lucroCambiosBrl = computed(() => (simCambios.value * props.config.valorKFeira) / Math.max(props.config.moedasPorK, 0.0001) - r.value.custoBrl)
const lucroPixBrl = computed(() => simPix.value - r.value.custoBrl)
const margemCambios = computed(() => (r.value.custoBrl > 0 ? (lucroCambiosBrl.value / r.value.custoBrl) * 100 : 0))
const margemPix = computed(() => (r.value.custoBrl > 0 ? (lucroPixBrl.value / r.value.custoBrl) * 100 : 0))
const margemIdealPix = computed(() =>
  r.value.pixMultiplicador > 0 && props.config.dolarPadraoCompra > 0
    ? (r.value.pixMultiplicador / props.config.dolarPadraoCompra - 1) * 100
    : props.config.margemVendaCambiosPercent,
)

function tone(margem: number, ideal: number) {
  return margem >= ideal ? 'text-success' : margem >= 5 ? 'text-warning' : 'text-danger'
}
</script>

<template>
  <div class="grid gap-3 rounded-2xl border border-line px-5 py-4 text-[15px]" aria-live="polite">
    <div class="flex items-center justify-between gap-2"><span class="text-muted">Custo BRL</span><span class="font-medium text-ink tabular">{{ formatMoney(r.custoBrl) }}</span></div>
    <div class="flex items-center justify-between gap-2"><span class="text-muted">Custo em Câmbios</span><span class="font-medium text-ink tabular">{{ formatInt(r.custoCambios) }} C</span></div>

    <hr class="border-line" />

    <EditableValue v-model="rawCambios" label="Sugestão Câmbios" :display="`${formatInt(simCambios)} C`" :placeholder="String(r.precoVendaCambios)" :overridden="rawCambios !== ''" inputmode="numeric" />
    <div class="flex items-center justify-between gap-2">
      <span class="text-muted">Lucro (Câmbios)</span>
      <span :class="['text-right tabular', lucroCambiosBrl > 0 ? 'font-semibold text-ink' : 'text-ink']">{{ formatInt(simCambios - r.custoCambios) }} C · {{ formatMoney(lucroCambiosBrl) }}</span>
    </div>
    <div class="flex items-center justify-between gap-2">
      <span class="text-muted">Margem</span>
      <span :class="['text-right tabular', tone(margemCambios, config.margemVendaCambiosPercent)]">
        <span class="font-semibold">{{ formatPercent(margemCambios) }}</span>
        <span v-if="margemCambios < config.margemVendaCambiosPercent" class="ml-1.5 text-[13px] opacity-70">(ideal {{ formatPercent(config.margemVendaCambiosPercent) }})</span>
      </span>
    </div>

    <hr class="border-line" />

    <EditableValue v-model="rawPix" label="Sugestão PIX" :display="formatMoney(simPix)" :placeholder="formatDecimal(r.precoVendaPix)" :overridden="rawPix !== ''" />
    <div class="flex items-center justify-between gap-2">
      <span class="text-muted">Lucro (PIX)</span>
      <span :class="['text-right tabular', lucroPixBrl > 0 ? 'font-semibold text-ink' : 'text-ink']">{{ formatMoney(lucroPixBrl) }}</span>
    </div>
    <div class="flex items-center justify-between gap-2">
      <span class="text-muted">Margem</span>
      <span :class="['text-right tabular', tone(margemPix, margemIdealPix)]">
        <span class="font-semibold">{{ formatPercent(margemPix) }}</span>
        <span v-if="margemPix < margemIdealPix" class="ml-1.5 text-[13px] opacity-70">(ideal {{ formatPercent(margemIdealPix) }})</span>
      </span>
    </div>
    <div class="flex items-center justify-between gap-2"><span class="text-muted">Multiplicador PIX</span><span class="font-medium text-ink tabular">× {{ formatDecimal(r.pixMultiplicador) }}</span></div>
    <p v-if="r.pixAjustadoPeloPiso && !rawPix" class="rounded-xl bg-warning-soft px-3 py-2 text-[13px] text-warning">PIX ajustado pelo piso: a faixa daria menos que o custo mínimo.</p>
  </div>
</template>
