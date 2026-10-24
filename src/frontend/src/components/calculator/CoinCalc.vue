<script setup lang="ts">
import { ArrowLeftRight } from 'lucide-vue-next'
import { computed, ref } from 'vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiInput from '@/components/ui/UiInput.vue'
import { formatInt, formatMoney, parseDecimal } from '@/lib/format'
import type { ConfigCalculo } from '@/lib/types'

const props = defineProps<{ config: ConfigCalculo }>()

type KType = 'feira' | 'trocas'
const kType = ref<KType>('feira')
const brl = ref('')
const coins = ref('')
const lastEdited = ref<'brl' | 'coins'>('brl')

const valorK = computed(() => (kType.value === 'feira' ? props.config.valorKFeira : props.config.valorKTrocas))
const kOptions = computed(() => [
  { value: 'feira' as const, label: `Feira · ${formatMoney(props.config.valorKFeira)}` },
  { value: 'trocas' as const, label: `Trocas · ${formatMoney(props.config.valorKTrocas)}` },
])

const coinsFromBrl = (v: number) => Math.round((v * props.config.moedasPorK) / Math.max(valorK.value, 0.0001))
const brlFromCoins = (v: number) => (v * valorK.value) / Math.max(props.config.moedasPorK, 0.0001)
const fixed = (v: number) => String(Math.round(v * 100) / 100).replace('.', ',')

function recompute() {
  if (lastEdited.value === 'brl') {
    const v = parseDecimal(brl.value)
    coins.value = v && v > 0 ? String(coinsFromBrl(v)) : ''
  } else {
    const v = parseDecimal(coins.value)
    brl.value = v && v > 0 ? fixed(brlFromCoins(v)) : ''
  }
}
function onBrl(v: string | number | null | undefined) { brl.value = String(v ?? ''); lastEdited.value = 'brl'; recompute() }
function onCoins(v: string | number | null | undefined) { coins.value = String(v ?? ''); lastEdited.value = 'coins'; recompute() }

const brlNum = computed(() => parseDecimal(brl.value) ?? 0)
const coinsNum = computed(() => parseDecimal(coins.value) ?? 0)
</script>

<template>
  <div class="grid gap-3">
    <UiChips v-model="kType" :options="kOptions" label="Tipo de K" compact @update:model-value="recompute" />

    <div class="grid gap-3 sm:grid-cols-2">
      <div class="grid gap-2">
        <UiInput :model-value="brl" label="Valor em R$" prefix="R$" inputmode="decimal" placeholder="Ex: 100" @update:model-value="onBrl" />
        <div v-if="brlNum > 0 && lastEdited === 'brl'" class="rounded-xl bg-surface-2 px-3 py-2">
          <p class="text-[13px] text-muted">Recebe em moedas</p>
          <p class="text-lg font-semibold text-ink tabular">{{ formatInt(coinsNum) }} C</p>
        </div>
      </div>
      <div class="grid gap-2">
        <UiInput :model-value="coins" label="Moedas (C)" inputmode="numeric" placeholder="Ex: 500" @update:model-value="onCoins" />
        <div v-if="coinsNum > 0 && lastEdited === 'coins'" class="rounded-xl bg-surface-2 px-3 py-2">
          <p class="text-[13px] text-muted">Valor em R$</p>
          <p class="text-lg font-semibold text-ink tabular">{{ formatMoney(brlNum) }}</p>
        </div>
      </div>
    </div>

    <div class="flex items-center gap-2.5 rounded-xl border border-line px-3 py-2 text-[13px] text-muted">
      <ArrowLeftRight class="size-4 shrink-0" />
      <span class="min-w-0">{{ formatInt(config.moedasPorK) }} moedas por K ({{ kType === 'feira' ? 'Feira' : 'Trocas' }}) · {{ formatMoney(valorK) }} por K</span>
    </div>
  </div>
</template>
