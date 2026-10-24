<script setup lang="ts">
import { ChevronLeft, ChevronRight } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import UiSwitch from '@/components/ui/UiSwitch.vue'
import { api } from '@/lib/api'
import { formatDay, formatMoney, todayIso } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { LinhaResumoDiario, ResumoDiario } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

/** Resumo diário de caixa: entrada, saída, saldo do dia e acumulado, com projeção opcional (mapa de calor no saldo acumulado). */
const mes = ref(todayIso().slice(0, 7))
const projetar = ref(false)
const periodo = ref('30')
const periodos = [
  { value: '15', label: '15 dias' }, { value: '30', label: '30 dias' }, { value: '90', label: '90 dias' },
  { value: '120', label: '120 dias' }, { value: '365', label: '1 ano' }, { value: '1825', label: '5 anos' },
]

const dados = useAsync((signal) =>
  api.get<ResumoDiario>('/api/financeiro/diario', { mes: mes.value, projecao: projetar.value ? periodo.value : undefined }, signal))
watch([mes, projetar, periodo], () => void dados.run(), { immediate: true })
watch(dados.error, (e) => e && toast.error('Não foi possível carregar o resumo', e.message))

const mesAtual = todayIso().slice(0, 7)
function mover(delta: number) {
  const [y, m] = mes.value.split('-').map(Number)
  const d = new Date(Date.UTC(y!, m! - 1 + delta, 1))
  const novo = `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}`
  if (novo <= mesAtual) mes.value = novo
}
const tituloMes = computed(() => {
  const [y, m] = mes.value.split('-').map(Number)
  return new Date(Date.UTC(y!, m! - 1, 1, 12)).toLocaleDateString('pt-BR', { month: 'long', year: 'numeric', timeZone: 'UTC' })
})

const linhas = computed(() => dados.data.value?.linhas ?? [])
const maxSaida = computed(() => Math.max(0, ...linhas.value.map((l) => l.saida)))
const maxAbs = computed(() => Math.max(1, ...linhas.value.map((l) => Math.abs(l.saldoAcumulado))))

/** Mapa de calor: verde para saldo positivo, vermelho para negativo; projetado fica mais claro. */
function calor(l: LinhaResumoDiario) {
  const forca = Math.min(1, Math.abs(l.saldoAcumulado) / maxAbs.value)
  const cor = l.saldoAcumulado >= 0 ? 'var(--color-success)' : 'var(--color-danger)'
  const pct = Math.round((l.projetado ? 8 : 14) + forca * (l.projetado ? 26 : 40))
  return { backgroundColor: `color-mix(in srgb, ${cor} ${pct}%, transparent)` }
}
const saidaGrande = (l: LinhaResumoDiario) => maxSaida.value > 0 && l.saida >= maxSaida.value * 0.7 && l.saida > 0

function rotulo(l: LinhaResumoDiario) {
  return l.inicio === l.fim ? formatDay(l.inicio) : `${formatDay(l.inicio)} – ${formatDay(l.fim)}`
}
const rotuloColuna = computed(() => ({ dia: 'Dia', semana: 'Semana', mes: 'Mês' })[dados.data.value?.projecaoDias ? dados.data.value.granularidadeProjecao : 'dia'])

const stats = computed<Stat[]>(() => {
  const d = dados.data.value
  if (!d) return []
  const t = d.totais
  const base: Stat[] = [
    { label: 'Entradas do mês', value: t.entradas, tone: 'success' },
    { label: 'Saídas do mês', value: t.saidas, tone: 'danger' },
    { label: 'Saldo hoje', value: t.saldoAtual, tone: t.saldoAtual >= 0 ? 'success' : 'danger' },
  ]
  if (!d.projecaoDias) return base
  return [
    ...base,
    { label: `Saldo em ${d.projecaoDias} dias`, value: t.saldoFinalProjetado, tone: t.saldoFinalProjetado >= 0 ? 'success' : 'danger', note: `média de ${formatMoney(t.mediaFaturamentoDia)} em vendas por dia` },
    { label: 'Menor saldo projetado', value: t.menorSaldoProjetado, tone: t.menorSaldoProjetado >= 0 ? 'default' : 'danger', note: t.diaMenorSaldo ? `em ${formatDay(t.diaMenorSaldo)}` : undefined },
  ]
})
</script>

<template>
  <PageCard title="Resumo diário" subtitle="Entradas, saídas e saldo dia a dia. Ligue a projeção para ver o caixa dos próximos dias pela média de vendas e lucro.">
    <div class="mb-6 flex flex-wrap items-center justify-between gap-4">
      <div v-if="!projetar" class="flex items-center gap-2">
        <button type="button" class="grid size-9 place-items-center rounded-full border border-line hover:bg-surface-2" aria-label="Mês anterior" @click="mover(-1)"><ChevronLeft class="size-4" /></button>
        <span class="min-w-40 text-center text-base font-semibold capitalize text-ink">{{ tituloMes }}</span>
        <button type="button" class="grid size-9 place-items-center rounded-full border border-line hover:bg-surface-2 disabled:opacity-40" aria-label="Próximo mês" :disabled="mes >= mesAtual" @click="mover(1)"><ChevronRight class="size-4" /></button>
      </div>
      <span v-else class="text-base font-semibold capitalize text-ink">{{ tituloMes }} e os próximos {{ periodos.find((p) => p.value === periodo)?.label }}</span>
      <div class="flex flex-wrap items-center gap-4">
        <UiChips v-if="projetar" v-model="periodo" :options="periodos" label="Período da projeção" compact />
        <UiSwitch v-model="projetar" label="Projeção" />
      </div>
    </div>

    <StatBlocks :stats="stats" :loading="!dados.data.value" :skeletons="projetar ? 5 : 3" />

    <UiSkeleton v-if="!dados.data.value" class="mt-8 h-64 w-full" />
    <UiEmptyState
      v-else-if="!linhas.length" class="mt-8"
      :title="dados.data.value.primeiroDia ? `Sem movimento antes de ${formatDay(dados.data.value.primeiroDia).slice(0, 5)}` : 'Sem movimento ainda'"
      description="O resumo começa no primeiro dia com lançamento."
    />
    <div v-else class="mt-8 overflow-x-auto rounded-3xl border border-line">
      <table class="w-full border-collapse text-left text-sm tabular">
        <thead class="bg-surface-2">
          <tr class="text-muted">
            <th class="px-4 py-3 font-medium">{{ rotuloColuna }}</th>
            <th class="px-4 py-3 text-right font-medium">Entrada</th>
            <th class="px-4 py-3 text-right font-medium">Saída</th>
            <th class="px-4 py-3 text-right font-medium">Saldo</th>
            <th class="px-4 py-3 text-right font-medium">Saldo acumulado</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-line">
          <tr v-for="l in linhas" :key="l.inicio" :class="l.projetado && 'italic'">
            <td class="whitespace-nowrap px-4 py-2.5 text-ink">
              {{ rotulo(l) }}
              <span v-if="l.projetado" class="ml-2 rounded-full border border-dashed border-line-strong px-2 py-0.5 text-xs not-italic text-muted">projeção</span>
            </td>
            <td class="px-4 py-2.5 text-right text-success">{{ formatMoney(l.entrada) }}</td>
            <td :class="['px-4 py-2.5 text-right text-danger', saidaGrande(l) && 'bg-danger-soft font-semibold']">{{ formatMoney(l.saida) }}</td>
            <td :class="['px-4 py-2.5 text-right', l.saldo >= 0 ? 'text-success' : 'text-danger']">{{ formatMoney(l.saldo) }}</td>
            <td
              :class="['px-4 py-2.5 text-right font-medium text-ink', l.projetado && 'border-l border-dashed border-line-strong']"
              :style="calor(l)"
            >{{ formatMoney(l.saldoAcumulado) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-if="projetar && dados.data.value" class="mt-4 text-sm text-muted">
      Linhas em itálico são projeção: média dos últimos 30 dias de vendas ({{ formatMoney(dados.data.value.totais.mediaFaturamentoDia) }}/dia) e de custo, mais lançamentos futuros já cadastrados
      ({{ formatMoney(dados.data.value.totais.conhecidoEntradas) }} a entrar, {{ formatMoney(dados.data.value.totais.conhecidoSaidas) }} a sair).
      Períodos longos são somados por semana ou mês.
    </p>
  </PageCard>
</template>
