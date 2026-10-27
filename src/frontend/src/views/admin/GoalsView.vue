<script setup lang="ts">
import { Flame, Target } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import DefinirMetasModal from '@/components/admin/metas/DefinirMetasModal.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import { api } from '@/lib/api'
import { formatDay, formatMoney, formatPercent } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { DiaMeta, PainelMetas } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'
import { useAuthStore } from '@/stores/auth'
import { addDays } from '@/lib/period'

/** Meta semanal de lucro: cadastro por semana, acompanhamento da semana atual, histórico e mapa de calor, sempre sobre as vendas pagas ou concluídas do Gestão. */
const auth = useAuthStore()
const painel = useAsync((signal) => api.get<PainelMetas>('/api/metas-semanais', undefined, signal))
const tab = ref<'atual' | 'historico' | 'tendencia'>('atual')
const tabs = [
  { value: 'atual' as const, label: 'Semana atual' },
  { value: 'historico' as const, label: 'Histórico' },
  { value: 'tendencia' as const, label: 'Tendência' },
]
const modal = ref(false)
const DIAS = ['Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb', 'Dom']

void painel.run()
watch(painel.error, (e) => e && toast.error('Não foi possível carregar a meta semanal', e.message))
const dados = ref<PainelMetas | null>(null)
watch(painel.data, (d) => (dados.value = d ?? dados.value))
function salvo(p: PainelMetas) {
  dados.value = p
}

const r = computed(() => dados.value?.resumo)
const semanaAtual = computed(() => {
  const p = dados.value
  if (!p) return []
  return p.dias.slice(-7).map((d, i) => ({ ...d, rotulo: DIAS[i]!, futuro: d.dia > p.hoje, hoje: d.dia === p.hoje }))
})
const melhorDia = computed(() => {
  const com = semanaAtual.value.filter((d) => !d.futuro && d.vendas > 0)
  return com.length ? com.reduce((a, b) => (b.lucroBrl > a.lucroBrl ? b : a)) : null
})
const tendencia = computed(() => {
  const p = dados.value
  return p?.lucroSemanaAnteriorBrl ? ((p.resumo.lucroBrl - p.lucroSemanaAnteriorBrl) / p.lucroSemanaAnteriorBrl) * 100 : null
})
const stats = computed<Stat[]>(() => {
  const x = r.value
  const p = dados.value
  if (!x || !p) return []
  return [
    { label: 'Lucro da semana', value: x.lucroBrl, note: `de ${formatMoney(x.metaBrl)}` },
    { label: 'Falta para a meta', value: x.faltaBrl, note: x.faltaBrl > 0 ? `${formatPercent(100 - x.progressoPercent)} restante` : 'Meta atingida' },
    { label: 'Média diária', value: x.mediaDiariaBrl, note: 'considerando os dias já passados' },
    { label: 'Projeção de fechamento', value: x.projecaoFechamentoBrl, note: 'com base na média atual' },
    {
      label: 'Necessário até sábado', value: x.status === 'batida' ? 0 : x.necessarioPorDiaUtilBrl,
      note: x.diasUteisRestantes ? `${x.diasUteisRestantes} ${x.diasUteisRestantes === 1 ? 'dia útil restante' : 'dias úteis restantes'}` : 'sem dias úteis restantes',
    },
  ]
})

const calor = computed(() => {
  const p = dados.value
  if (!p) return { semanas: [] as { inicio: string; dias: (DiaMeta & { futuro: boolean })[] }[], max: 1 }
  const semanas = []
  for (let i = 0; i < p.dias.length; i += 7) {
    const dias = p.dias.slice(i, i + 7).map((d) => ({ ...d, futuro: d.dia > p.hoje }))
    semanas.push({ inicio: dias[0]!.dia, dias })
  }
  return { semanas, max: Math.max(...p.dias.map((d) => d.lucroBrl), 1) }
})
const maxHistorico = computed(() => Math.max(...(dados.value?.historico ?? []).flatMap((h) => [h.lucroBrl, h.metaBrl]), 1))

function semanaRotulo(inicio: string) {
  return `${formatDay(inicio).slice(0, 6)} a ${formatDay(addDays(inicio, 6)).slice(0, 6)}`
}
const statusMsg = computed(() => {
  const x = r.value
  if (!x) return null
  if (x.status === 'batida') return { tom: 'bg-success-soft text-success', texto: 'Meta atingida. O objetivo semanal foi concluído.' }
  if (x.status === 'ultimoDia') return { tom: 'bg-warning-soft text-warning', texto: `Hoje é domingo: ainda dá para fechar a semana com ${formatMoney(x.faltaBrl)} a mais.` }
  return {
    tom: 'bg-surface-2 text-text',
    texto: `Faltam ${formatMoney(x.faltaBrl)} para bater a meta.${x.diasUteisRestantes ? ` São ${formatMoney(x.necessarioPorDiaUtilBrl)} por dia até sábado para garantir a folga de domingo.` : ''}`,
  }
})
</script>

<template>
  <PageCard title="Meta semanal" :subtitle="dados ? `Segunda a domingo · ${formatDay(dados.semanaInicio)} a ${formatDay(dados.semanaFim)}` : 'Acompanhe o lucro da semana contra a meta.'">
    <template v-if="auth.isAdmin" #actions>
      <UiButton size="lg" :disabled="!dados" @click="modal = true"><Target class="size-4" /> Definir meta</UiButton>
    </template>

    <UiSkeleton v-if="!dados" class="h-40 w-full" />
    <template v-else>
      <UiTabs v-model="tab" :tabs="tabs" class="mb-6" />

      <div v-if="tab === 'atual' && r" class="flex flex-col gap-8">
        <p v-if="statusMsg" :class="['rounded-2xl px-5 py-4 text-sm', statusMsg.tom]" role="status">{{ statusMsg.texto }}</p>
        <StatBlocks :stats="stats" />

        <section aria-labelledby="progresso-title">
          <div class="mb-3 flex flex-wrap items-end justify-between gap-3">
            <div>
              <h2 id="progresso-title" class="text-[15px] text-muted">Progresso da meta</h2>
              <p class="mt-1 text-[34px] leading-none font-semibold tracking-tight text-ink tabular">{{ formatMoney(r.lucroBrl) }}</p>
            </div>
            <span class="text-lg font-semibold text-ink tabular">{{ formatPercent(r.progressoPercent) }}</span>
          </div>
          <div class="relative h-4 overflow-hidden rounded-full bg-surface-3" role="progressbar" :aria-valuenow="Math.round(r.progressoPercent)" aria-valuemin="0" aria-valuemax="100">
            <div :class="['h-full rounded-full', r.status === 'batida' ? 'bg-success' : 'bg-brand']" :style="{ width: `${r.progressoPercent}%` }" />
            <div class="absolute top-0 h-full w-0.5 bg-ink/50" :style="{ left: `${r.ritmoIdealPercent}%` }" :title="`Ritmo ideal: ${Math.round(r.ritmoIdealPercent)}%`" />
          </div>
          <div class="mt-3 flex flex-wrap justify-between gap-2 text-[13px] text-muted">
            <span>{{ formatMoney(r.lucroBrl) }} realizado</span>
            <span>| = ritmo ideal (segunda a domingo)</span>
            <span>Meta: {{ formatMoney(r.metaBrl) }}</span>
          </div>
          <p v-if="!dados.planejamento[0]?.definida" class="mt-3 text-sm text-muted">Esta semana usa a meta padrão de {{ formatMoney(dados.padraoBrl) }}. {{ auth.isAdmin ? 'Use “Definir meta” para trocar.' : 'Um administrador pode definir outra.' }}</p>
        </section>

        <section aria-labelledby="dia-title">
          <h2 id="dia-title" class="text-lg font-semibold text-ink">Dia a dia</h2>
          <p class="mb-4 text-sm text-muted">
            <template v-if="melhorDia">Melhor dia: {{ melhorDia.rotulo }} ({{ formatMoney(melhorDia.lucroBrl) }})</template>
            <template v-else>Nenhuma venda na semana ainda.</template>
          </p>
          <div class="grid grid-cols-2 gap-3 min-[480px]:grid-cols-4 lg:grid-cols-7">
            <div
              v-for="d in semanaAtual" :key="d.dia"
              :class="['flex flex-col items-center gap-1 rounded-xl border px-2 py-4 text-center', d.hoje ? 'border-sel-ring bg-brand-soft' : 'border-line', d.futuro && 'opacity-40']"
            >
              <span class="text-[13px] font-semibold text-ink">{{ d.rotulo }}</span>
              <span class="text-xs text-muted">{{ formatDay(d.dia).slice(0, 6) }}</span>
              <span :class="['mt-2 text-sm font-semibold tabular', d.lucroBrl > 0 ? 'text-ink' : 'text-muted']">{{ d.futuro ? '—' : formatMoney(d.lucroBrl) }}</span>
              <span v-if="!d.futuro && d.vendas" class="text-xs text-muted">{{ d.vendas }} venda{{ d.vendas === 1 ? '' : 's' }}</span>
            </div>
          </div>
        </section>
      </div>

      <div v-else-if="tab === 'historico'">
        <UiEmptyState v-if="!dados.historico.length" title="Sem histórico ainda" description="As semanas com vendas ou meta definida aparecem aqui, com lucro, meta e resultado." />
        <div v-else class="overflow-x-auto">
          <table class="w-full min-w-[640px] text-[15px]">
            <thead>
              <tr class="border-b border-line text-left text-[13px] font-semibold text-ink">
                <th class="py-3 pr-4">Semana</th><th class="px-4 py-3 text-right">Vendas</th><th class="px-4 py-3 text-right">Receita</th>
                <th class="px-4 py-3 text-right">Lucro</th><th class="px-4 py-3 text-right">Meta</th><th class="py-3 pl-4 text-right">Resultado</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-line">
              <tr v-for="h in dados.historico" :key="h.semanaInicio" :class="h.atual && 'font-semibold'">
                <td class="py-3.5 pr-4"><span class="inline-flex flex-wrap items-center gap-2">{{ semanaRotulo(h.semanaInicio) }}<UiBadge v-if="h.atual" tone="neutral">atual</UiBadge></span></td>
                <td class="px-4 py-3.5 text-right text-muted tabular">{{ h.vendas }}</td>
                <td class="px-4 py-3.5 text-right text-muted tabular">{{ formatMoney(h.receitaBrl) }}</td>
                <td class="px-4 py-3.5 text-right tabular">{{ formatMoney(h.lucroBrl) }}</td>
                <td class="px-4 py-3.5 text-right text-muted tabular">{{ formatMoney(h.metaBrl) }}</td>
                <td class="py-3.5 pl-4 text-right"><UiBadge :tone="h.bateu ? 'success' : 'neutral'">{{ h.bateu ? 'Bateu' : formatPercent(Math.floor((h.lucroBrl / h.metaBrl) * 100)) }}</UiBadge></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div v-else-if="tab === 'tendencia'" class="flex flex-col gap-8">
        <StatBlocks
          :stats="[
            { label: 'Sequência batendo a meta', text: dados.sequencia ? `${dados.sequencia} semana${dados.sequencia === 1 ? '' : 's'}` : '—', note: dados.sequencia ? 'seguidas, sem contar esta' : 'bata a meta para começar uma sequência', tone: dados.sequencia ? 'success' : 'default' },
            { label: 'Contra a semana anterior', text: tendencia == null ? '—' : `${tendencia >= 0 ? '+' : ''}${formatPercent(tendencia)}`, note: dados.lucroSemanaAnteriorBrl != null ? `semana passada: ${formatMoney(dados.lucroSemanaAnteriorBrl)}` : 'sem histórico', tone: tendencia != null && tendencia > 0 ? 'success' : 'default' },
            { label: 'Semanas batidas', text: `${dados.historico.filter((h) => h.bateu).length} de ${dados.historico.length}`, note: 'no histórico' },
          ]"
        />
        <section aria-labelledby="calor-title">
          <h2 id="calor-title" class="text-lg font-semibold text-ink"><Flame class="mr-1.5 inline size-5 align-text-bottom" />Mapa de calor</h2>
          <p class="mb-4 text-sm text-muted">Lucro por dia: quanto mais forte a cor, maior o lucro.</p>
          <div class="overflow-x-auto">
            <div class="grid min-w-[480px] gap-1" :style="{ gridTemplateColumns: `3rem repeat(${calor.semanas.length}, minmax(0, 1fr))` }">
              <template v-for="(nome, i) in DIAS" :key="nome">
                <span class="self-center text-xs text-muted">{{ nome }}</span>
                <div
                  v-for="s in calor.semanas" :key="s.inicio"
                  class="relative aspect-square overflow-hidden rounded-md bg-surface-3"
                  :title="`${formatDay(s.dias[i]!.dia)}: ${s.dias[i]!.futuro ? 'ainda não chegou' : formatMoney(s.dias[i]!.lucroBrl)}`"
                >
                  <div v-if="!s.dias[i]!.futuro && s.dias[i]!.lucroBrl > 0" class="absolute inset-0 bg-brand" :style="{ opacity: 0.2 + 0.8 * (s.dias[i]!.lucroBrl / calor.max) }" />
                </div>
              </template>
            </div>
          </div>
        </section>
        <section v-if="dados.historico.length" aria-labelledby="semanas-title">
          <h2 id="semanas-title" class="mb-4 text-lg font-semibold text-ink">Lucro por semana</h2>
          <div class="flex flex-col gap-3">
            <div v-for="h in [...dados.historico].reverse()" :key="h.semanaInicio" class="grid grid-cols-[minmax(0,6.5rem)_1fr_auto] items-center gap-3 text-sm">
              <span :class="['truncate', h.atual ? 'font-semibold text-ink' : 'text-muted']">{{ semanaRotulo(h.semanaInicio) }}</span>
              <div class="relative h-3 overflow-hidden rounded-full bg-surface-3">
                <div :class="['h-full rounded-full', h.bateu ? 'bg-success' : 'bg-brand']" :style="{ width: `${Math.min(100, (h.lucroBrl / maxHistorico) * 100)}%` }" />
                <div class="absolute top-0 h-full w-0.5 bg-ink/50" :style="{ left: `${Math.min(100, (h.metaBrl / maxHistorico) * 100)}%` }" />
              </div>
              <span class="text-right font-medium text-ink tabular">{{ formatMoney(h.lucroBrl) }}</span>
            </div>
          </div>
          <p class="mt-3 text-[13px] text-muted">A barra vertical marca a meta de cada semana.</p>
        </section>
      </div>
    </template>

    <DefinirMetasModal v-model:open="modal" :semanas="dados?.planejamento ?? []" @saved="salvo" />
  </PageCard>
</template>
