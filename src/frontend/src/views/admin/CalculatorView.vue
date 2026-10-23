<script setup lang="ts">
import { CircleHelp, RefreshCw, TriangleAlert } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import CoinCalc from '@/components/calculator/CoinCalc.vue'
import NftResult from '@/components/calculator/NftResult.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import { api } from '@/lib/api'
import { ESMERALDAS_POR_K, dolarCompraParaCalculo, esmeraldasParaUsd } from '@/lib/calculator'
import { formatDecimal, formatInt, formatMoney, parseDecimal } from '@/lib/format'
import type { RegrasPrecificacao } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const { data, loading, error, run } = useAsync((signal) => api.get<RegrasPrecificacao>('/api/precificacao/regras', undefined, signal))
void run()

const tab = ref<'nft' | 'moedas'>('nft')
const tabs = [
  { value: 'nft' as const, label: 'Calc NFT' },
  { value: 'moedas' as const, label: 'Moedas' },
]
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
const helpOpen = ref(false)
</script>

<template>
  <PageCard title="Calculadora" subtitle="Custo e venda sugerida em Câmbios e PIX, ou conversão de moedas.">
    <template #actions>
      <UiButton variant="ghost" :aria-expanded="helpOpen" @click="helpOpen = !helpOpen"><CircleHelp class="size-4" /> Sobre o custo</UiButton>
    </template>

    <UiEmptyState v-if="error && !data" :icon="TriangleAlert" title="Não foi possível carregar as regras" :description="error.message">
      <UiButton variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
    </UiEmptyState>

    <UiSkeleton v-else-if="loading && !data" class="h-80 max-w-xl" />

    <div v-else-if="data" class="max-w-xl">
      <p v-if="helpOpen" class="mb-5 rounded-2xl bg-info-soft px-4 py-3 text-[14px] leading-relaxed text-info">
        O custo usa sempre o dólar de compra fixo de Preços e regras ({{ formatMoney(dolarCompraParaCalculo(data.configuracao)) }}), não a cotação ao vivo nem o custo médio do estoque.
      </p>
      <p v-if="data.versao === 0" class="mb-5 rounded-2xl bg-warning-soft px-4 py-3 text-[14px] text-warning">
        As regras ainda não foram salvas: os valores abaixo são os padrões.
      </p>

      <UiTabs v-model="tab" :tabs="tabs" class="mb-6" />

      <div v-if="tab === 'nft'" class="grid gap-5">
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

        <NftResult :custo-usd="custoUsd" :config="data.configuracao" :faixas="data.faixasPix" />
      </div>

      <CoinCalc v-else :config="data.configuracao" />
    </div>
  </PageCard>
</template>
