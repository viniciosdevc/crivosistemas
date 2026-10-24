<script setup lang="ts">
import { SlidersHorizontal } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import AjustePosicaoModal from '@/components/admin/posicao/AjustePosicaoModal.vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import { formatDay, formatDecimal, formatMoney, formatUsd } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { PosicaoAtual, PosicaoDiaria } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

/** Posição financeira, como a do crivonft: patrimônio de agora, ajuste dos saldos reais e capital dia a dia. */
const atual = useAsync((signal) => api.get<PosicaoAtual>('/api/posicao/atual', undefined, signal))
const dias = useAsync((signal) => api.get<PosicaoDiaria[]>('/api/posicao', undefined, signal))
void atual.run()
void dias.run()
watch(atual.error, (e) => e && toast.error('Não foi possível carregar a posição', e.message))
watch(dias.error, (e) => e && toast.error('Não foi possível carregar o histórico', e.message))

const open = ref(false)
function refresh() {
  void atual.run()
  void dias.run()
}

const p = computed(() => atual.data.value?.patrimonio ?? null)
const hoje = computed(() => atual.data.value?.hoje ?? null)

const partes = computed(() => {
  const v = p.value
  if (!v) return []
  return [
    { key: 'cambios', label: 'Câmbios', color: 'bg-info', valor: v.cambiosBrl },
    { key: 'usd', label: 'Dólares', color: 'bg-brand', valor: v.usdBrl },
    { key: 'esm', label: 'Esmeraldas', color: 'bg-success', valor: v.esmeraldasBrl },
    { key: 'conta', label: 'Conta em R$', color: 'bg-warning', valor: v.contaBrl },
  ]
})
const somaPositiva = computed(() => partes.value.reduce((a, x) => a + Math.max(0, x.valor), 0))
const pct = (v: number) => (somaPositiva.value > 0 ? (Math.max(0, v) / somaPositiva.value) * 100 : 0)

const ativos = computed(() => {
  const v = p.value
  if (!v) return []
  return [
    { label: 'Câmbios', principal: formatDecimal(v.cambiosSaldo), brl: v.cambiosBrl, sub: `${formatMoney(v.brlPorCambio * 1000)} a cada mil câmbios` },
    {
      label: 'Dólares em estoque', principal: formatUsd(v.usdSaldo), brl: v.usdBrl,
      sub: `${v.usdVendas > 0 ? 'Venda média' : 'Dólar de venda (sem vendas)'} ${formatMoney(v.usdPrecoMedioVenda)}/US$ · custo ${formatMoney(v.usdCustoMedio * v.usdSaldo)}`,
    },
    {
      label: 'Esmeraldas', principal: formatDecimal(v.esmeraldasSaldo), brl: v.esmeraldasBrl,
      sub: v.esmeraldasSaldo > 0 ? `Custo médio US$ ${v.esmeraldaCustoUsd.toFixed(4)}/unid.` : 'Sem estoque',
    },
    {
      label: 'Conta em R$', principal: formatMoney(v.contaBrl), brl: v.contaBrl,
      sub: v.pendenteReceber > 0 || v.pendentePagar > 0
        ? [v.pendenteReceber > 0 ? `+ ${formatMoney(v.pendenteReceber)} a receber` : null, v.pendentePagar > 0 ? `− ${formatMoney(v.pendentePagar)} a pagar` : null].filter(Boolean).join(' · ')
        : `Entradas ${formatMoney(v.totalEntradas)} · saídas ${formatMoney(v.totalSaidas)}`,
    },
  ]
})

const apuracao = computed(() => {
  const v = p.value
  if (!v) return []
  return [
    { label: 'Patrimônio operacional', valor: formatMoney(v.totalBrl) },
    { label: 'Caixa + câmbios líquidos', valor: formatMoney(v.caixaLiquido) },
    { label: 'Estoque não caixa', valor: formatMoney(v.usdBrl + v.esmeraldasBrl) },
    { label: 'Pendente a receber', valor: formatMoney(v.pendenteReceber) },
    { label: 'Pendente a pagar', valor: formatMoney(v.pendentePagar) },
    { label: 'Liquidez', valor: `${v.liquidezPercent.toFixed(1)}%` },
  ]
})

const ajustes = computed(() => {
  const h = hoje.value
  if (!h) return []
  const f = (v: number | null, fmt: (n: number) => string) => (v == null ? '—' : Math.abs(v) < 0.0001 ? 'sem diferença' : `${v > 0 ? '+' : '−'}${fmt(Math.abs(v))}`)
  return [
    { label: 'Câmbios', valor: f(h.ajusteCambios, formatDecimal), v: h.ajusteCambios },
    { label: 'Dólares', valor: f(h.ajusteUsd, formatUsd), v: h.ajusteUsd },
    { label: 'Esmeraldas', valor: f(h.ajusteEsmeraldas, formatDecimal), v: h.ajusteEsmeraldas },
    { label: 'Conta em R$', valor: f(h.ajusteBrl, formatMoney), v: h.ajusteBrl },
  ]
})
const tom = (v: number | null) => (v == null || Math.abs(v) < 0.0001 ? 'text-muted' : v > 0 ? 'text-success' : 'text-danger')

// Capital dia a dia: posições lançadas (mais antiga primeiro) com a variação do total em relação ao dia anterior lançado.
const historico = computed(() => {
  const l = [...(dias.data.value ?? [])].sort((a, b) => a.data.localeCompare(b.data))
  return l.map((d, i) => ({ ...d, variacao: i > 0 ? d.totalBrl - l[i - 1]!.totalBrl : null })).reverse()
})
type Linha = (typeof historico.value)[number]
const columns: Column[] = [
  { key: 'data', label: 'Dia' },
  { key: 'brlFinal', label: 'Conta (R$)', align: 'right' },
  { key: 'usdFinal', label: 'Dólares', align: 'right', hideBelow: 'sm' },
  { key: 'cambiosFinal', label: 'Câmbios', align: 'right', hideBelow: 'md' },
  { key: 'esmeraldasFinal', label: 'Esmeraldas', align: 'right', hideBelow: 'lg' },
  { key: 'totalBrl', label: 'Total (R$)', align: 'right' },
  { key: 'variacao', label: 'Variação', align: 'right', hideBelow: 'sm' },
]
</script>

<template>
  <PageCard title="Posição financeira" subtitle="Snapshot dos ativos em câmbios, dólares, esmeraldas e conta em R$. Informe os saldos reais e o sistema lança a diferença.">
    <div v-if="!p" class="flex flex-col gap-4"><UiSkeleton class="h-24" /><UiSkeleton class="h-40" /></div>
    <template v-else>
      <section class="flex flex-wrap items-end justify-between gap-4">
        <div class="min-w-0 flex-1">
          <p class="text-sm font-medium text-muted">Total consolidado</p>
          <p class="mt-1 text-4xl font-semibold tracking-tight text-ink tabular">{{ formatMoney(p.totalBrl) }}</p>
          <div v-if="somaPositiva > 0" class="mt-4 flex h-2 w-full max-w-xl overflow-hidden rounded-full bg-surface-muted">
            <div v-for="x in partes" :key="x.key" class="h-full" :class="x.color" :style="{ width: `${pct(x.valor)}%` }" />
          </div>
          <ul class="mt-3 flex flex-wrap gap-x-5 gap-y-1 text-sm text-muted">
            <li v-for="x in partes" :key="x.key" class="flex items-center gap-2"><span class="size-2 rounded-full" :class="x.color" />{{ x.label }} <span class="tabular text-ink">{{ pct(x.valor).toFixed(1) }}%</span></li>
          </ul>
        </div>
        <UiButton size="lg" @click="open = true"><SlidersHorizontal class="size-4" /> Ajustar posição</UiButton>
      </section>

      <section class="mt-8">
        <h2 class="mb-2 text-lg font-semibold text-ink">Ativos</h2>
        <div class="divide-y divide-line border-y border-line">
          <div v-for="a in ativos" :key="a.label" class="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1 py-4">
            <div class="min-w-0">
              <p class="font-medium text-ink">{{ a.label }}</p>
              <p class="text-sm text-muted">{{ a.sub }}</p>
            </div>
            <p class="text-right">
              <span class="block text-lg font-semibold text-ink tabular">{{ a.principal }}</span>
              <span class="block text-sm text-muted tabular">{{ formatMoney(a.brl) }}</span>
            </p>
          </div>
        </div>
      </section>

      <section v-if="hoje" class="mt-8">
        <h2 class="mb-2 text-lg font-semibold text-ink">Ajuste de hoje <span class="text-sm font-normal text-muted">({{ formatDay(hoje.data) }})</span></h2>
        <dl class="grid gap-x-8 gap-y-2 sm:grid-cols-2">
          <div v-for="a in ajustes" :key="a.label" class="flex justify-between border-b border-line py-2 text-sm">
            <dt class="text-muted">{{ a.label }}</dt>
            <dd class="font-semibold tabular" :class="tom(a.v)">{{ a.valor }}</dd>
          </div>
        </dl>
      </section>

      <section class="mt-8">
        <h2 class="mb-2 text-lg font-semibold text-ink">Leitura para apuração</h2>
        <dl class="divide-y divide-line border-y border-line">
          <div v-for="r in apuracao" :key="r.label" class="flex justify-between py-3 text-sm">
            <dt class="text-muted">{{ r.label }}</dt>
            <dd class="font-semibold text-ink tabular">{{ r.valor }}</dd>
          </div>
        </dl>
        <p class="mt-3 text-sm text-muted">Venda registrada não significa caixa disponível. Confira antes de distribuir.</p>
      </section>

      <section class="mt-8">
        <h2 class="mb-1 text-lg font-semibold text-ink">Capital dia a dia</h2>
        <p class="mb-3 text-sm text-muted">Total em reais (conta, dólares, câmbios e esmeraldas) em cada posição lançada, com a valoração daquele dia.</p>
        <UiEmptyState v-if="dias.data.value && dias.data.value.length === 0" title="Nenhuma posição lançada" description="Use “Ajustar posição” para lançar a de hoje e começar o histórico." />
        <DataTable v-else :columns="columns" :rows="historico" :row-key="(r: Linha) => r.id" :loading="dias.loading.value">
          <template #cell-data="{ row }"><span class="font-medium text-ink">{{ formatDay(row.data) }}</span></template>
          <template #cell-brlFinal="{ row }"><span class="tabular">{{ formatMoney(row.brlFinal) }}</span></template>
          <template #cell-usdFinal="{ row }"><span class="tabular">{{ formatUsd(row.usdFinal) }}</span></template>
          <template #cell-cambiosFinal="{ row }"><span class="tabular">{{ formatDecimal(row.cambiosFinal) }}</span></template>
          <template #cell-esmeraldasFinal="{ row }"><span class="tabular">{{ formatDecimal(row.esmeraldasFinal) }}</span></template>
          <template #cell-totalBrl="{ row }"><span class="tabular font-semibold text-ink">{{ formatMoney(row.totalBrl) }}</span></template>
          <template #cell-variacao="{ row }">
            <span v-if="row.variacao == null" class="text-muted">—</span>
            <span v-else class="tabular" :class="row.variacao >= 0 ? 'text-success' : 'text-danger'">{{ row.variacao >= 0 ? '+' : '−' }}{{ formatMoney(Math.abs(row.variacao)) }}</span>
          </template>
        </DataTable>
      </section>
    </template>
    <AjustePosicaoModal v-if="p" v-model:open="open" :patrimonio="p" :hoje="hoje" @saved="refresh" />
  </PageCard>
</template>
