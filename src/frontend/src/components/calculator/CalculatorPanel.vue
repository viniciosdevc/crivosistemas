<script setup lang="ts">
/** Conteúdo da calculadora (Calc NFT ou Moedas), usado na página e no modal de ações rápidas. */
import { computed, ref, watch } from 'vue'
import CoinCalc from '@/components/calculator/CoinCalc.vue'
import NftResult from '@/components/calculator/NftResult.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiInput from '@/components/ui/UiInput.vue'
import { ESMERALDAS_POR_K, dolarCompraParaCalculo, esmeraldasParaUsd } from '@/lib/calculator'
import { formatDecimal, formatInt, formatMoney, parseDecimal } from '@/lib/format'
import type { RegrasPrecificacao } from '@/lib/types'

const props = defineProps<{ mode: 'nft' | 'moedas'; regras: RegrasPrecificacao; showHelp?: boolean }>()

const modo = ref<'dolar' | 'esmeralda'>('dolar')
const modos = [
  { value: 'dolar' as const, label: 'Por dólar' },
  { value: 'esmeralda' as const, label: 'Por esmeralda' },
]

// ── por dólar ───────────────────────────────────────────────────────────────
const rawUsd = ref('10')
const custoUsdDolar = computed(() => Math.max(parseDecimal(rawUsd.value) ?? 0, 0))

// ── por esmeralda ───────────────────────────────────────────────────────────
const K_KEY = 'crivo:calc:valor-k-esmeralda-usd'
function readK() {
  try {
    return localStorage.getItem(K_KEY) ?? ''
  } catch {
    return ''
  }
}
const rawK = ref(readK())
watch(rawK, (v) => {
  try {
    localStorage.setItem(K_KEY, v)
  } catch {
    /* sem armazenamento: segue sem lembrar */
  }
})
const rawQtd = ref('1000')
const custoUsdEsmeralda = computed(() => esmeraldasParaUsd(Math.max(parseDecimal(rawQtd.value) ?? 0, 0), Math.max(parseDecimal(rawK.value) ?? 0, 0)))

const custoUsd = computed(() => (modo.value === 'dolar' ? custoUsdDolar.value : custoUsdEsmeralda.value))
</script>

<template>
  <div>
    <p v-if="showHelp" class="mb-5 rounded-2xl bg-info-soft px-4 py-3 text-[14px] leading-relaxed text-info">
      O custo usa sempre o dólar de compra fixo de Preços e regras ({{ formatMoney(dolarCompraParaCalculo(props.regras.configuracao)) }}), não a cotação ao vivo nem o custo médio do estoque.
    </p>
    <p v-if="regras.versao === 0" class="mb-5 rounded-2xl bg-warning-soft px-4 py-3 text-[14px] text-warning">
      As regras ainda não foram salvas: os valores abaixo são os padrões.
    </p>

    <div v-if="mode === 'nft'" class="grid gap-5">
      <UiChips v-model="modo" :options="modos" label="Modo de cálculo" />

      <UiInput v-if="modo === 'dolar'" v-model="rawUsd" label="Custo USD" prefix="$" inputmode="decimal" placeholder="0,00" />
      <template v-else>
        <div class="grid gap-4 sm:grid-cols-2">
          <UiInput v-model="rawK" :label="`Valor do K ($ / ${formatInt(ESMERALDAS_POR_K)})`" prefix="$" inputmode="decimal" placeholder="Ex: 9,44" />
          <UiInput v-model="rawQtd" label="Esmeraldas" inputmode="numeric" placeholder="Ex: 300" />
        </div>
        <div class="flex items-center justify-between gap-2 rounded-2xl border border-line px-5 py-4 text-[15px]">
          <span class="text-muted">Custo USD</span><span class="font-medium text-ink tabular">$ {{ formatDecimal(Math.round(custoUsdEsmeralda * 100) / 100) }}</span>
        </div>
      </template>

      <NftResult :custo-usd="custoUsd" :config="regras.configuracao" :faixas="regras.faixasPix" />
    </div>

    <CoinCalc v-else :config="regras.configuracao" />
  </div>
</template>
