<script setup lang="ts">
import { ArrowDownCircle, ArrowUpCircle, Check, Clock, FilterX, Plus, Wallet, XCircle } from 'lucide-vue-next'
import { computed, onMounted, ref, watch } from 'vue'
import LancamentoModal from '@/components/admin/finance/LancamentoModal.vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import UiSearch from '@/components/ui/UiSearch.vue'
import UiSelect from '@/components/ui/UiSelect.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { ApiError, api } from '@/lib/api'
import { confirmWithReason } from '@/lib/confirm'
import { formatDay, formatMoney, todayIso } from '@/lib/format'
import { origemLancamento, statusLancamento } from '@/lib/labels'
import { isPeriodPreset, periodOptions, resolvePeriod, type PeriodPreset } from '@/lib/period'
import { toast } from '@/lib/toast'
import type { Lancamento, Paged, ResumoFinanceiro, TipoLancamento } from '@/lib/types'
import { useUrlFilters } from '@/lib/urlFilters'
import { useAsync } from '@/lib/useAsync'

const pageSize = 15
const defaults = { periodo: '30d', de: '', ate: '', tipo: '', status: '', origem: '', busca: '', page: '1' }
const { filters, set, reset } = useUrlFilters(defaults)

const preset = computed<PeriodPreset>(() => (isPeriodPreset(filters.value.periodo) ? filters.value.periodo : '30d'))
const range = computed(() => resolvePeriod(preset.value, todayIso(), { de: filters.value.de, ate: filters.value.ate }))

const tipoOptions = [
  { value: '', label: 'Entradas e saídas' },
  { value: 'entrada', label: 'Entradas' },
  { value: 'saida', label: 'Saídas' },
]
const statusOptions = [
  { value: '', label: 'Todos os status' },
  { value: 'confirmado', label: 'Confirmado' },
  { value: 'pendente', label: 'Pendente' },
  { value: 'cancelado', label: 'Cancelado' },
]
const origemOptions = [
  { value: '', label: 'Todas as origens' },
  { value: 'manual', label: 'Manual' },
  { value: 'venda', label: 'Venda PIX' },
  { value: 'venda_cambios', label: 'Venda de câmbios' },
  { value: 'compra_usd', label: 'Compra de dólar' },
]

const resumo = useAsync((signal) => api.get<ResumoFinanceiro>('/api/financeiro/resumo', { de: range.value.de, ate: range.value.ate }, signal))
const lista = useAsync((signal) =>
  api.get<Paged<Lancamento>>('/api/financeiro/lancamentos', {
    de: range.value.de, ate: range.value.ate, tipo: filters.value.tipo, status: filters.value.status, origem: filters.value.origem, busca: filters.value.busca,
    page: Number(filters.value.page) || 1, pageSize,
  }, signal),
)
const categorias = useAsync((signal) => api.get<string[]>('/api/financeiro/categorias', undefined, signal))

watch(range, () => void resumo.run(), { immediate: true })
watch([range, () => filters.value.tipo, () => filters.value.status, () => filters.value.origem, () => filters.value.busca, () => filters.value.page], () => void lista.run(), { immediate: true })
for (const a of [resumo, lista]) watch(a.error, (e) => e && toast.error('Não foi possível carregar o financeiro', e.message))

// Traz o que já existia antes do financeiro (vendas com PIX, compras de dólar). Idempotente.
onMounted(async () => {
  try {
    const r = await api.post<{ vendas: number; compras: number }>('/api/financeiro/sincronizar')
    if (r.vendas + r.compras > 0) refresh()
  } catch { /* sem efeito: o que já está lançado continua valendo */ }
  void categorias.run()
})

function refresh() {
  void resumo.run()
  void lista.run()
  void categorias.run()
}

const hasFilters = computed(() => ['tipo', 'status', 'origem', 'busca'].some((k) => filters.value[k as keyof typeof defaults] !== '') || preset.value !== '30d')

const columns: Column[] = [
  { key: 'data', label: 'Data' },
  { key: 'descricao', label: 'Lançamento' },
  { key: 'origem', label: 'Origem', hideBelow: 'md' },
  { key: 'status', label: 'Status', hideBelow: 'sm' },
  { key: 'valorBrl', label: 'Valor', align: 'right' },
  { key: 'acoes', label: '', align: 'right' },
]

const cards = computed(() => {
  const r = resumo.data.value
  if (!r) return []
  return [
    { label: 'Saldo atual', value: formatMoney(r.saldo), icon: Wallet, tone: r.saldo >= 0 ? 'text-ink' : 'text-danger', hint: 'Só lançamentos confirmados' },
    { label: 'Entradas no período', value: formatMoney(r.periodo.entradas), icon: ArrowUpCircle, tone: 'text-success', hint: `Hoje: ${formatMoney(r.hoje.entradas)}` },
    { label: 'Saídas no período', value: formatMoney(r.periodo.saidas), icon: ArrowDownCircle, tone: 'text-danger', hint: `Hoje: ${formatMoney(r.hoje.saidas)}` },
    {
      label: 'Resultado do período', value: formatMoney(r.periodo.resultado), icon: Clock, tone: r.periodo.resultado >= 0 ? 'text-success' : 'text-danger',
      hint: r.pendentes > 0 ? `${r.pendentes} pendente(s): ${formatMoney(r.pendenteReceber)} a receber, ${formatMoney(r.pendentePagar)} a pagar` : `Mês: ${formatMoney(r.mes.resultado)}`,
    },
  ]
})

const maxCategoria = computed(() => Math.max(1, ...(resumo.data.value?.porCategoria ?? []).map((c) => c.total)))

const modalOpen = ref(false)
const modalTipo = ref<TipoLancamento>('saida')
function novo(tipo: TipoLancamento) { modalTipo.value = tipo; modalOpen.value = true }

async function confirmar(l: Lancamento) {
  try {
    await api.post(`/api/financeiro/lancamentos/${l.id}/confirmar`)
    toast.success(l.tipo === 'entrada' ? 'Entrada confirmada' : 'Saída confirmada')
    refresh()
  } catch (e) {
    toast.error('Não foi possível confirmar', e instanceof ApiError ? e.message : undefined)
  }
}

async function cancelar(l: Lancamento) {
  const motivo = await confirmWithReason({
    title: 'Cancelar este lançamento?',
    description: `${formatMoney(l.valorBrl)} (${l.categoria}) sai do saldo e fica no histórico como cancelado.`,
    confirmLabel: 'Cancelar lançamento',
    reasonLabel: 'Motivo (obrigatório)',
    danger: true,
  })
  if (motivo === null) return
  if (!motivo) return void toast.error('Informe o motivo do cancelamento')
  try {
    await api.post(`/api/financeiro/lancamentos/${l.id}/cancelar`, { motivo })
    toast.success('Lançamento cancelado')
    refresh()
  } catch (e) {
    toast.error('Não foi possível cancelar', e instanceof ApiError ? e.message : undefined)
  }
}

function origemLink(l: Lancamento) {
  if (l.vendaId) return `/painel/vendas/${l.vendaId}`
  if (l.loteEstoqueId) return '/painel/estoque'
  if (l.movimentoCambiosId) return '/painel/cambios'
  return null
}
</script>

<template>
  <PageCard title="Financeiro" subtitle="Entradas e saídas em reais. O PIX das vendas e as compras de dólar entram sozinhos; o resto você lança aqui.">
    <template #actions>
      <UiButton @click="novo('saida')"><Plus class="size-4" /> Novo lançamento</UiButton>
    </template>

    <div class="mb-5 flex flex-wrap items-end gap-3">
      <UiChips :model-value="preset" :options="periodOptions" label="Período" @update:model-value="(v) => set({ periodo: v })" />
      <template v-if="preset === 'custom'">
        <UiDatePicker label="De" :model-value="filters.de || range.de" :max="range.ate" @update:model-value="(v) => set({ de: v ?? '' })" />
        <UiDatePicker label="Até" :model-value="filters.ate || range.ate" :min="range.de" @update:model-value="(v) => set({ ate: v ?? '' })" />
      </template>
    </div>

    <div v-if="!resumo.data.value" class="mb-8 grid gap-4 sm:grid-cols-2 xl:grid-cols-4"><UiSkeleton v-for="i in 4" :key="i" class="h-28" /></div>
    <dl v-else class="mb-8 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <div v-for="c in cards" :key="c.label" class="rounded-2xl border border-line p-5">
        <dt class="flex items-center gap-2 text-sm text-muted"><component :is="c.icon" class="size-4" /> {{ c.label }}</dt>
        <dd :class="['mt-1 text-2xl font-semibold tracking-tight tabular', c.tone]">{{ c.value }}</dd>
        <dd class="mt-1 text-[13px] text-muted">{{ c.hint }}</dd>
      </div>
    </dl>

    <section v-if="resumo.data.value && resumo.data.value.porCategoria.length" class="mb-8" aria-labelledby="cat-title">
      <h2 id="cat-title" class="mb-3 text-lg font-semibold text-ink">Por categoria</h2>
      <ul class="grid gap-3 md:grid-cols-2">
        <li v-for="c in resumo.data.value.porCategoria" :key="c.categoria + c.tipo" class="rounded-2xl border border-line px-4 py-3">
          <div class="flex items-baseline justify-between gap-3 text-sm">
            <span class="font-medium text-ink">{{ c.categoria }} <span class="font-normal text-muted">· {{ c.quantidade }}</span></span>
            <span :class="['tabular font-medium', c.tipo === 'entrada' ? 'text-success' : 'text-danger']">{{ c.tipo === 'entrada' ? '+' : '−' }}{{ formatMoney(c.total) }}</span>
          </div>
          <div class="mt-2 h-1.5 overflow-hidden rounded-full bg-surface-3">
            <div :class="['h-full rounded-full', c.tipo === 'entrada' ? 'bg-success' : 'bg-danger']" :style="{ width: `${Math.max(3, (c.total / maxCategoria) * 100)}%` }" />
          </div>
        </li>
      </ul>
    </section>

    <section aria-labelledby="mov-title">
      <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 id="mov-title" class="text-lg font-semibold text-ink">Lançamentos</h2>
        <UiSearch :model-value="filters.busca" placeholder="Categoria ou descrição" @update:model-value="(v) => set({ busca: v })" />
      </div>
      <form class="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-4" @submit.prevent>
        <UiSelect label="Tipo" :model-value="filters.tipo" :options="tipoOptions" @update:model-value="(v) => set({ tipo: v })" />
        <UiSelect label="Status" :model-value="filters.status" :options="statusOptions" @update:model-value="(v) => set({ status: v })" />
        <UiSelect label="Origem" :model-value="filters.origem" :options="origemOptions" @update:model-value="(v) => set({ origem: v })" />
        <div class="flex items-end">
          <UiButton variant="ghost" :disabled="!hasFilters" class="w-full" @click="reset"><FilterX class="size-4" /> Limpar filtros</UiButton>
        </div>
      </form>

      <DataTable :columns="columns" :rows="lista.data.value?.items" :row-key="(l) => l.id" :loading="lista.loading.value" caption="Lançamentos financeiros">
        <template #cell-data="{ row }"><span class="font-medium text-ink">{{ formatDay(row.data) }}</span></template>
        <template #cell-descricao="{ row }">
          <div class="min-w-0">
            <p :class="['font-medium text-ink', row.status === 'cancelado' && 'line-through opacity-60']">{{ row.categoria }}</p>
            <p class="line-clamp-2 max-w-80 text-sm text-muted">
              {{ row.status === 'cancelado' && row.motivoCancelamento ? `Cancelado: ${row.motivoCancelamento}` : (row.descricao ?? '—') }}
              <RouterLink v-if="origemLink(row)" :to="origemLink(row)!" class="ml-1 font-medium text-ink hover:underline">Ver origem</RouterLink>
            </p>
          </div>
        </template>
        <template #cell-origem="{ row }"><StatusBadge :map="origemLancamento" :value="row.origem" /></template>
        <template #cell-status="{ row }"><StatusBadge :map="statusLancamento" :value="row.status" /></template>
        <template #cell-valorBrl="{ row }">
          <span :class="['tabular font-semibold', row.status === 'cancelado' ? 'text-muted line-through' : row.tipo === 'entrada' ? 'text-success' : 'text-danger']">
            {{ row.tipo === 'entrada' ? '+' : '−' }}{{ formatMoney(row.valorBrl) }}
          </span>
        </template>
        <template #cell-acoes="{ row }">
          <div v-if="!row.automatico && row.status !== 'cancelado'" class="flex justify-end gap-1">
            <UiButton v-if="row.status === 'pendente'" variant="ghost" size="icon-sm" title="Confirmar" :aria-label="`Confirmar ${row.categoria}`" @click="confirmar(row)"><Check class="size-4 text-success" /></UiButton>
            <UiButton variant="ghost" size="icon-sm" title="Cancelar" :aria-label="`Cancelar ${row.categoria}`" @click="cancelar(row)"><XCircle class="size-4 text-danger" /></UiButton>
          </div>
        </template>
        <template #empty>
          <UiEmptyState title="Nenhum lançamento" description="Nada neste período com esses filtros. Registre um lançamento ou amplie o período." />
        </template>
        <template #footer>
          <UiPagination v-if="lista.data.value" :page="lista.data.value.page" :page-size="lista.data.value.pageSize" :total="lista.data.value.total" @update:page="(p) => set({ page: String(p) })" />
        </template>
      </DataTable>
    </section>

    <LancamentoModal v-model:open="modalOpen" :categorias="categorias.data.value ?? []" :tipo-inicial="modalTipo" @saved="refresh" />
  </PageCard>
</template>
