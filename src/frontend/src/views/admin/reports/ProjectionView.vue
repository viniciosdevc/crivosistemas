<script setup lang="ts">
import { Pencil, Plus, Trash2 } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import CenarioModal from '@/components/admin/projecao/CenarioModal.vue'
import ProjecaoChart from '@/components/admin/projecao/ProjecaoChart.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import { confirm } from '@/lib/confirm'
import { formatDay, formatMoney, formatPercent } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { Projecao, ProjecaoResumida } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

/** Projeção: cenários salvos a partir da média diária de vendas e lucro, acompanhados contra o que realmente vendeu. */
const lista = useAsync((signal) => api.get<ProjecaoResumida[]>('/api/projecoes', undefined, signal))
const selecionado = ref<string | null>(null)
const detalhe = useAsync((signal) => api.get<Projecao>(`/api/projecoes/${selecionado.value}`, undefined, signal))
const metrica = ref<'lucro' | 'caixa'>('lucro')
const metricas = [
  { value: 'lucro' as const, label: 'Lucro acumulado' },
  { value: 'caixa' as const, label: 'Caixa' },
]
const modal = ref(false)
const editando = ref(false)

void lista.run().then(() => {
  selecionado.value ??= lista.data.value?.[0]?.cenario.id ?? null
})
watch(selecionado, (id) => id && void detalhe.run(), { immediate: true })
watch(lista.error, (e) => e && toast.error('Não foi possível carregar os cenários', e.message))
watch(detalhe.error, (e) => e && toast.error('Não foi possível carregar o cenário', e.message))

const p = computed(() => (selecionado.value ? detalhe.data.value : null))

const stats = computed<Stat[]>(() => {
  const r = p.value?.resumo
  const c = p.value?.cenario
  if (!r || !c) return []
  const dif = r.diferencaLucro
  return [
    { label: 'Lucro projetado', value: r.lucroFinal, note: `até ${formatDay(c.dataFinal)}` },
    { label: 'Caixa projetado', value: r.caixaFinal, note: `parte de ${formatMoney(c.caixaInicialBrl)} hoje` },
    { label: 'Realizado até hoje', value: r.lucroRealizadoAteHoje, note: r.diasDecorridos ? `${r.diasDecorridos} de ${r.diasTotal} dias` : 'Ainda não começou' },
    {
      label: 'Contra o esperado', value: dif, tone: r.diasDecorridos === 0 ? 'default' : dif >= 0 ? 'success' : 'danger',
      note: r.diasDecorridos ? `esperado ${formatMoney(r.lucroEsperadoAteHoje)}` : undefined,
    },
  ]
})

const serie = computed(() => {
  const x = p.value
  if (!x) return []
  const inicial = x.cenario.caixaInicialBrl
  return x.dias.map((d) =>
    metrica.value === 'lucro'
      ? { dia: d.dia, projetado: d.lucroProjetado, realizado: d.lucroRealizado }
      : { dia: d.dia, projetado: d.caixaProjetado, realizado: d.lucroRealizado == null ? null : inicial + d.lucroRealizado },
  )
})

function novo() {
  editando.value = false
  modal.value = true
}
function editar() {
  editando.value = true
  modal.value = true
}
async function salvo(salvo: Projecao) {
  await lista.run()
  if (selecionado.value === salvo.cenario.id) void detalhe.run()
  else selecionado.value = salvo.cenario.id
}
async function excluir() {
  const c = p.value?.cenario
  if (!c?.id) return
  if (!(await confirm({ title: `Apagar “${c.nome}”?`, description: 'O cenário some da lista. Vendas e financeiro não mudam.', confirmLabel: 'Apagar', danger: true }))) return
  try {
    await api.delete(`/api/projecoes/${c.id}`)
    toast.success('Cenário apagado')
    selecionado.value = null
    await lista.run()
    selecionado.value = lista.data.value?.[0]?.cenario.id ?? null
  } catch (e) {
    toast.error('Não foi possível apagar', e instanceof Error ? e.message : undefined)
  }
}
</script>

<template>
  <PageCard title="Projeção" subtitle="Projete o lucro e o caixa pela média diária de um período e acompanhe o cenário contra o que realmente vendeu.">
    <template #actions>
      <UiButton size="lg" @click="novo"><Plus class="size-4" /> Novo cenário</UiButton>
    </template>

    <UiSkeleton v-if="!lista.data.value" class="h-40 w-full" />
    <UiEmptyState
      v-else-if="!lista.data.value.length" title="Nenhum cenário ainda"
      description="Escolha um período base, até quando projetar e um ajuste de crescimento. Crie quantos cenários quiser para comparar."
    >
      <UiButton size="lg" @click="novo"><Plus class="size-4" /> Criar o primeiro cenário</UiButton>
    </UiEmptyState>
    <template v-else>
      <div class="divide-y divide-line border-y border-line" role="listbox" aria-label="Cenários">
        <button
          v-for="x in lista.data.value" :key="x.cenario.id!" type="button" role="option" :aria-selected="x.cenario.id === selecionado"
          :class="['flex w-full flex-wrap items-baseline justify-between gap-x-6 gap-y-1 px-3 py-3 text-left transition-colors hover:bg-surface-2', x.cenario.id === selecionado && 'bg-brand-soft']"
          @click="selecionado = x.cenario.id"
        >
          <span class="min-w-0">
            <span class="block font-medium text-ink">{{ x.cenario.nome }}</span>
            <span class="block text-sm text-muted">
              {{ formatDay(x.cenario.inicioProjecao) }} a {{ formatDay(x.cenario.dataFinal) }} · base {{ formatDay(x.cenario.baseDe) }} a {{ formatDay(x.cenario.baseAte) }}
              <template v-if="x.cenario.crescimentoPercent"> · {{ x.cenario.crescimentoPercent > 0 ? '+' : '' }}{{ formatPercent(x.cenario.crescimentoPercent) }}</template>
            </span>
          </span>
          <span class="text-right">
            <span class="block font-semibold text-ink tabular">{{ formatMoney(x.resumo.lucroFinal) }}</span>
            <span v-if="x.resumo.diasDecorridos" class="block text-sm tabular" :class="x.resumo.diferencaLucro >= 0 ? 'text-success' : 'text-danger'">
              {{ x.resumo.diferencaLucro >= 0 ? '+' : '−' }}{{ formatMoney(Math.abs(x.resumo.diferencaLucro)) }} no ritmo
            </span>
            <span v-else class="block text-sm text-muted">não começou</span>
          </span>
        </button>
      </div>

      <section v-if="p" class="mt-8" aria-labelledby="cenario-title">
        <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
          <h2 id="cenario-title" class="text-lg font-semibold text-ink">{{ p.cenario.nome }}</h2>
          <div class="flex flex-wrap items-center gap-2">
            <UiChips v-model="metrica" :options="metricas" label="Gráfico" compact />
            <UiButton variant="subtle" @click="editar"><Pencil class="size-4" /> Editar</UiButton>
            <UiButton variant="subtle" @click="excluir"><Trash2 class="size-4" /> Apagar</UiButton>
          </div>
        </div>
        <StatBlocks :stats="stats" :skeletons="4" />
        <div class="mt-8">
          <ProjecaoChart :dias="serie" :label="`Gráfico de ${metrica === 'lucro' ? 'lucro acumulado' : 'caixa'} projetado e realizado, dia a dia`" />
          <p class="mt-4 text-sm text-muted">
            Média do período base: {{ formatMoney(p.cenario.mediaFaturamentoDia) }} de vendas e {{ formatMoney(p.cenario.mediaLucroDia) }} de lucro por dia.
            O caixa soma o lucro projetado ao caixa de quando o cenário foi salvo — não considera despesas fixas nem compras de estoque.
          </p>
        </div>
      </section>
      <UiSkeleton v-else-if="selecionado" class="mt-8 h-64 w-full" />
    </template>

    <CenarioModal v-model:open="modal" :cenario="editando ? p?.cenario : null" @saved="salvo" />
  </PageCard>
</template>
