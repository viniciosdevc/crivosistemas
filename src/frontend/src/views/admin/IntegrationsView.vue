<script setup lang="ts">
import { CalendarSync, Plug, RefreshCw, TriangleAlert } from 'lucide-vue-next'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import EventModal from '@/components/admin/integrations/EventModal.vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import UiSearch from '@/components/ui/UiSearch.vue'
import UiSelect from '@/components/ui/UiSelect.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import { ApiError, api } from '@/lib/api'
import { confirm } from '@/lib/confirm'
import { formatDateTime, formatDay, formatInt, pluralize, todayIso } from '@/lib/format'
import { estadoEnvio, estadoEvento, estadoReconciliacao, tipoEvento, tipoReconciliacao } from '@/lib/labels'
import { addDays } from '@/lib/period'
import { toast } from '@/lib/toast'
import type { EventoIntegracao, Paged, Reconciliacao, StatusIntegracao } from '@/lib/types'
import { useUrlFilters } from '@/lib/urlFilters'
import { useAsync } from '@/lib/useAsync'

const pageSize = 15
const defaults = { aba: 'eventos', estado: '', tipo: '', busca: '', page: '1' }
const { filters, set } = useUrlFilters(defaults)
const tab = computed<'eventos' | 'reconciliacoes'>({
  get: () => (filters.value.aba === 'reconciliacoes' ? 'reconciliacoes' : 'eventos'),
  set: (v) => set({ aba: v === 'eventos' ? '' : v, estado: '', tipo: '', busca: '' }),
})
const tabs = [
  { value: 'eventos' as const, label: 'Eventos' },
  { value: 'reconciliacoes' as const, label: 'Reconciliações' },
]

// ── status ──────────────────────────────────────────────────────────────────
const status = useAsync((signal) => api.get<StatusIntegracao>('/api/integracoes/crivonft/status', undefined, signal))
void status.run()
const stateOrder = ['recebido', 'processado', 'falhou', 'rejeitado'] as const

// ── eventos ─────────────────────────────────────────────────────────────────
const eventQuery = computed(() => ({ estado: filters.value.estado, tipo: filters.value.tipo, busca: filters.value.busca, page: Number(filters.value.page) || 1, pageSize }))
const events = useAsync((signal) => api.get<Paged<EventoIntegracao>>('/api/integracoes/crivonft/eventos', eventQuery.value, signal))
watch([eventQuery, tab], () => tab.value === 'eventos' && void events.run(), { immediate: true })
watch(events.error, (e) => e && toast.error('Não foi possível carregar os eventos', e.message))

const stateOptions = [{ value: '', label: 'Todos os estados' }, ...stateOrder.map((s) => ({ value: s as string, label: estadoEvento[s]!.label }))]
const typeOptions = [{ value: '', label: 'Todos os tipos' }, ...Object.entries(tipoEvento).map(([value, label]) => ({ value, label }))]
const eventColumns: Column[] = [
  { key: 'recebidoEm', label: 'Recebido em' },
  { key: 'tipo', label: 'Evento' },
  { key: 'estado', label: 'Estado' },
  { key: 'origem', label: 'Origem', hideBelow: 'md' },
  { key: 'tentativas', label: 'Tentativas', align: 'right', hideBelow: 'lg' },
]
const selected = ref<string | null>(null)
const modalOpen = ref(false)
function openEvent(id: string) {
  selected.value = id
  modalOpen.value = true
}
function eventsChanged() {
  void events.run()
  void status.run()
}

// ── reconciliações ──────────────────────────────────────────────────────────
const recPage = ref(1)
const recs = useAsync((signal) => api.get<Paged<Reconciliacao>>('/api/integracoes/crivonft/reconciliacoes', { page: recPage.value, pageSize: 10 }, signal))
watch([recPage, tab], () => tab.value === 'reconciliacoes' && void recs.run(), { immediate: true })
const recFrom = ref(addDays(todayIso(), -2))
const recTo = ref(todayIso())
const recRangeError = computed(() => (recFrom.value && recTo.value && recFrom.value > recTo.value ? 'A data inicial deve ser anterior à final.' : null))
const requesting = ref(false)
async function requestReconciliation() {
  if (recRangeError.value || !recFrom.value || !recTo.value) return
  if (!(await confirm({
    title: 'Reconciliar este período?',
    description: `O Worker vai buscar no crivonft as vendas e clientes de ${formatDay(recFrom.value)} a ${formatDay(recTo.value)} e aplicar as diferenças. Pode demorar alguns minutos.`,
    confirmLabel: 'Reconciliar',
  }))) return
  requesting.value = true
  try {
    await api.post('/api/integracoes/crivonft/reconciliacoes', { de: recFrom.value, ate: recTo.value })
    toast.success('Reconciliação na fila', 'Acompanhe o andamento na lista abaixo.')
    recPage.value = 1
    await recs.run()
    void status.run()
  } catch (e) {
    toast.error('Não foi possível solicitar', e instanceof ApiError ? e.message : undefined)
  } finally {
    requesting.value = false
  }
}
const recColumns: Column[] = [
  { key: 'criadoEm', label: 'Pedida em' },
  { key: 'periodo', label: 'Período', hideBelow: 'sm' },
  { key: 'estado', label: 'Estado' },
  { key: 'vendas', label: 'Vendas', align: 'right', hideBelow: 'md' },
  { key: 'clientes', label: 'Clientes', align: 'right', hideBelow: 'lg' },
]

// Enquanto houver reconciliação na fila ou rodando, atualiza a lista a cada 5 s.
let timer: ReturnType<typeof setInterval> | undefined
const running = computed(() => recs.data.value?.items.some((r) => r.estado === 'pendente' || r.estado === 'executando') ?? false)
watch(
  [running, tab],
  ([isRunning, current]) => {
    clearInterval(timer)
    if (isRunning && current === 'reconciliacoes') {
      timer = setInterval(() => {
        void recs.run()
        void status.run()
      }, 5000)
    }
  },
  { immediate: true },
)
onBeforeUnmount(() => clearInterval(timer))

const s = computed(() => status.data.value)
</script>

<template>
  <PageCard title="Integrações" subtitle="Conexão com o crivonft: eventos recebidos, reconciliações e envio de preços.">
    <template #actions>
      <UiButton variant="secondary" :loading="status.loading.value" @click="status.run"><RefreshCw class="size-4" /> Atualizar</UiButton>
    </template>

    <UiEmptyState v-if="status.error.value && !s" :icon="TriangleAlert" title="Não foi possível carregar o status" :description="status.error.value.message">
      <UiButton variant="secondary" @click="status.run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
    </UiEmptyState>

    <template v-else>
      <div class="grid gap-4 lg:grid-cols-3">
        <section class="rounded-2xl border border-line p-5" aria-labelledby="conn-title">
          <h2 id="conn-title" class="mb-3 text-lg font-semibold text-ink">Conexão</h2>
          <UiSkeleton v-if="!s" class="h-24" />
          <template v-else>
            <ul class="space-y-2 text-[15px]">
              <li class="flex items-center justify-between gap-3">Receber eventos <UiBadge :tone="s.recebeEventos ? 'success' : 'warning'">{{ s.recebeEventos ? 'Configurado' : 'Não configurado' }}</UiBadge></li>
              <li class="flex items-center justify-between gap-3">Enviar preços <UiBadge :tone="s.envia ? 'success' : 'warning'">{{ s.envia ? 'Configurado' : 'Não configurado' }}</UiBadge></li>
              <li class="flex items-center justify-between gap-3">Reconciliar <UiBadge :tone="s.exporta ? 'success' : 'warning'">{{ s.exporta ? 'Configurado' : 'Não configurado' }}</UiBadge></li>
            </ul>
            <p class="mt-4 text-sm text-muted">
              Último evento: <span class="text-ink">{{ s.ultimoEventoRecebidoEm ? formatDateTime(s.ultimoEventoRecebidoEm) : 'nenhum ainda' }}</span>
            </p>
          </template>
        </section>

        <section class="rounded-2xl border border-line p-5" aria-labelledby="states-title">
          <h2 id="states-title" class="mb-3 text-lg font-semibold text-ink">Eventos por estado</h2>
          <UiSkeleton v-if="!s" class="h-24" />
          <div v-else class="grid grid-cols-2 gap-2">
            <button
              v-for="st in stateOrder"
              :key="st"
              type="button"
              class="rounded-xl bg-surface-2 p-3 text-left transition-colors hover:bg-surface-3"
              :aria-label="`${estadoEvento[st]!.label}: ${s.eventosPorEstado[st] ?? 0}. Filtrar a lista`"
              @click="set({ aba: '', estado: st })"
            >
              <span class="block text-xl font-semibold text-ink tabular">{{ formatInt(s.eventosPorEstado[st] ?? 0) }}</span>
              <StatusBadge :map="estadoEvento" :value="st" />
            </button>
          </div>
        </section>

        <section class="rounded-2xl border border-line p-5" aria-labelledby="last-title">
          <h2 id="last-title" class="mb-3 text-lg font-semibold text-ink">Últimas execuções</h2>
          <UiSkeleton v-if="!s" class="h-24" />
          <dl v-else class="space-y-3 text-[15px]">
            <div>
              <dt class="text-sm text-muted">Reconciliação</dt>
              <dd v-if="s.ultimaReconciliacao" class="flex flex-wrap items-center gap-2">
                <StatusBadge :map="estadoReconciliacao" :value="s.ultimaReconciliacao.estado" />
                <span class="text-text">{{ formatDateTime(s.ultimaReconciliacao.criadoEm) }}</span>
              </dd>
              <dd v-else class="text-faint">Nenhuma ainda</dd>
            </div>
            <div>
              <dt class="text-sm text-muted">Envio de preços (versão {{ s.versaoPrecificacao }})</dt>
              <dd v-if="s.ultimoEnvioPrecificacao" class="flex flex-wrap items-center gap-2">
                <StatusBadge :map="estadoEnvio" :value="s.ultimoEnvioPrecificacao.estado" />
                <span class="text-text">{{ formatDateTime(s.ultimoEnvioPrecificacao.enviadoEm ?? s.ultimoEnvioPrecificacao.criadoEm) }}</span>
              </dd>
              <dd v-else class="text-faint">Nenhum ainda</dd>
            </div>
          </dl>
        </section>
      </div>

      <section v-if="s?.ultimosErros.length" class="mt-4 rounded-2xl border border-line p-5" aria-labelledby="errors-title">
        <h2 id="errors-title" class="mb-3 text-lg font-semibold text-ink">Últimos erros</h2>
        <ul class="divide-y divide-line">
          <li v-for="e in s.ultimosErros" :key="e.id">
            <button type="button" class="flex w-full flex-col gap-1 py-3 text-left hover:bg-surface-2 sm:flex-row sm:items-center sm:gap-4" @click="openEvent(e.id)">
              <span class="flex shrink-0 items-center gap-2">
                <StatusBadge :map="estadoEvento" :value="e.estado" />
                <span class="text-sm text-muted">{{ formatDateTime(e.recebidoEm) }}</span>
              </span>
              <span class="min-w-0 flex-1 truncate text-[15px] text-text">{{ e.erro ?? tipoEvento[e.tipo] ?? e.tipo }}</span>
            </button>
          </li>
        </ul>
      </section>

      <UiTabs v-model="tab" :tabs="tabs" class="mt-8 mb-5" />

      <template v-if="tab === 'eventos'">
        <form class="mb-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-4" @submit.prevent>
          <UiSelect label="Estado" :model-value="filters.estado" :options="stateOptions" @update:model-value="(v) => set({ estado: v })" />
          <UiSelect label="Tipo" :model-value="filters.tipo" :options="typeOptions" @update:model-value="(v) => set({ tipo: v })" />
          <div class="flex flex-col gap-2 sm:col-span-2">
            <span class="text-sm font-medium text-text">Busca</span>
            <UiSearch class="sm:!w-full" :model-value="filters.busca" placeholder="Id do evento, id da venda, cliente ou erro" @update:model-value="(v) => set({ busca: v })" />
          </div>
        </form>

        <UiEmptyState v-if="events.error.value && !events.data.value" :icon="TriangleAlert" title="Não foi possível carregar os eventos" :description="events.error.value.message">
          <UiButton variant="secondary" @click="events.run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
        </UiEmptyState>
        <DataTable
          v-else
          :columns="eventColumns"
          :rows="events.data.value?.items"
          :loading="events.loading.value"
          :row-key="(e: EventoIntegracao) => e.id"
          caption="Eventos recebidos"
        >
          <template #cell-recebidoEm="{ row }">
            <button type="button" class="text-left font-medium text-ink hover:underline" @click="openEvent(row.id)">{{ formatDateTime(row.recebidoEm) }}</button>
          </template>
          <template #cell-tipo="{ row }">
            {{ tipoEvento[row.tipo] ?? row.tipo }}
            <p v-if="row.erro" class="max-w-xs truncate text-sm text-danger">{{ row.erro }}</p>
          </template>
          <template #cell-estado="{ row }"><StatusBadge :map="estadoEvento" :value="row.estado" /></template>
          <template #cell-origem="{ row }">
            <template v-if="row.origem">{{ row.origem }} <span class="text-muted">· {{ row.origemId }}</span></template>
            <span v-else class="text-faint">—</span>
          </template>
          <template #cell-tentativas="{ row }"><span class="tabular">{{ row.tentativas }}</span></template>
          <template #empty>
            <UiEmptyState :icon="Plug" title="Nenhum evento encontrado" description="Quando o crivonft enviar vendas e clientes, os eventos aparecem aqui." />
          </template>
          <template #footer>
            <UiPagination v-if="events.data.value" :page="events.data.value.page" :page-size="events.data.value.pageSize" :total="events.data.value.total" @update:page="(p) => set({ page: String(p) })" />
          </template>
        </DataTable>
      </template>

      <template v-else>
        <section class="mb-5 rounded-2xl border border-line p-5" aria-labelledby="rec-title">
          <h2 id="rec-title" class="text-lg font-semibold text-ink">Reconciliar um período</h2>
          <p class="mt-1 mb-4 text-sm text-muted">Busca no crivonft as vendas e clientes do período e corrige o que estiver diferente. A rodada noturna já cobre os últimos 3 dias.</p>
          <form class="grid items-start gap-3 sm:grid-cols-3" @submit.prevent="requestReconciliation">
            <UiDatePicker v-model="recFrom" label="De" :max="recTo" />
            <UiDatePicker v-model="recTo" label="Até" :min="recFrom" :error="recRangeError" />
            <div class="flex flex-col gap-2 sm:pt-[26px]">
              <UiButton type="submit" size="lg" :loading="requesting" :disabled="!!recRangeError || !recFrom || !recTo"><CalendarSync class="size-4" /> Solicitar</UiButton>
            </div>
          </form>
        </section>

        <UiEmptyState v-if="recs.error.value && !recs.data.value" :icon="TriangleAlert" title="Não foi possível carregar as reconciliações" :description="recs.error.value.message">
          <UiButton variant="secondary" @click="recs.run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
        </UiEmptyState>
        <DataTable v-else :columns="recColumns" :rows="recs.data.value?.items" :loading="recs.loading.value && !recs.data.value" :row-key="(r: Reconciliacao) => r.id" caption="Reconciliações">
          <template #cell-criadoEm="{ row }">
            <span class="font-medium text-ink">{{ formatDateTime(row.criadoEm) }}</span>
            <p class="text-sm text-muted">{{ tipoReconciliacao[row.tipo] ?? row.tipo }}</p>
          </template>
          <template #cell-periodo="{ row }">{{ row.de ? `${formatDay(row.de)} a ${formatDay(row.ate)}` : 'Tudo' }}</template>
          <template #cell-estado="{ row }">
            <StatusBadge :map="estadoReconciliacao" :value="row.estado" />
            <p v-if="row.erro" class="mt-1 max-w-xs text-sm break-words text-danger">{{ row.erro }}</p>
            <p v-else-if="row.concluidoEm" class="mt-1 text-sm text-muted">Concluída em {{ formatDateTime(row.concluidoEm) }}</p>
          </template>
          <template #cell-vendas="{ row }"><span class="tabular">{{ formatInt(row.vendasAplicadas) }} de {{ formatInt(row.vendasLidas) }}</span></template>
          <template #cell-clientes="{ row }"><span class="tabular">{{ pluralize(row.clientesCriados, 'novo', 'novos') }} de {{ formatInt(row.clientesLidos) }}</span></template>
          <template #empty>
            <UiEmptyState :icon="CalendarSync" title="Nenhuma reconciliação ainda" description="A rodada noturna e os pedidos feitos aqui aparecem nesta lista." />
          </template>
          <template #footer>
            <UiPagination v-if="recs.data.value" :page="recs.data.value.page" :page-size="recs.data.value.pageSize" :total="recs.data.value.total" @update:page="(p) => (recPage = p)" />
          </template>
        </DataTable>
      </template>
    </template>

    <EventModal v-model:open="modalOpen" :event-id="selected" @changed="eventsChanged" />
  </PageCard>
</template>
