<script setup lang="ts">
import { ArrowDown, ArrowUp, Layers, Plus, RefreshCw, Send, TriangleAlert } from 'lucide-vue-next'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import HabboPanel from '@/components/admin/nfts/HabboPanel.vue'
import NewCollectionModal from '@/components/admin/nfts/NewCollectionModal.vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import UiSearch from '@/components/ui/UiSearch.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import { ApiError, api } from '@/lib/api'
import { formatDateTime, formatInt, formatMoney, formatPercent, formatUsd } from '@/lib/format'
import { estadoEnvio } from '@/lib/labels'
import { toast } from '@/lib/toast'
import type { NftCatalogo, NftColecao, NftItem, Precificacao } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const tab = ref<'precos' | 'colecoes' | 'habbo'>('precos')
const tabs = computed(() => [
  { value: 'precos' as const, label: 'Preços', count: data.value?.mudaram || null },
  { value: 'colecoes' as const, label: 'Coleções' },
  { value: 'habbo' as const, label: 'Habbo' },
])

const categoria = ref('')
const busca = ref('')
const filtro = ref<'todos' | 'mudaram'>('todos')
const page = ref(1)
const pageSize = 25
const query = computed(() => ({ categoria: categoria.value, q: busca.value, apenasMudaram: filtro.value === 'mudaram' ? 1 : undefined, page: page.value, pageSize }))
const { data, loading, error, run } = useAsync((signal) => api.get<NftCatalogo>('/api/nfts', query.value, signal))
watch([categoria, busca, filtro], () => (page.value = 1))
watch(query, () => void run(), { immediate: true })

const regras = useAsync((signal) => api.get<Precificacao>('/api/precificacao', undefined, signal))
void regras.run()

const categoriaOptions = computed(() => [
  { value: '', label: 'Todas' },
  ...(data.value?.categorias ?? []).map((c) => ({ value: c.categoria, label: `${c.rotulo} (${formatInt(c.total)})` })),
])

// ── atualização do catálogo (feita pelo crivonft, em segundo plano) ─────────
const estadoCatalogo: Record<string, { label: string; tone: 'neutral' | 'info' | 'success' | 'danger' }> = {
  nunca_executado: { label: 'Nunca atualizado', tone: 'neutral' },
  executando: { label: 'Atualizando…', tone: 'info' },
  concluido: { label: 'Atualizado', tone: 'success' },
  erro: { label: 'Com erro', tone: 'danger' },
}
const running = computed(() => data.value?.status.estado === 'executando')
let timer: ReturnType<typeof setInterval> | undefined
watch(running, (v) => {
  clearInterval(timer)
  if (v) timer = setInterval(() => void run(), 6000)
})
onBeforeUnmount(() => clearInterval(timer))

const starting = ref(false)
async function atualizar() {
  starting.value = true
  try {
    await api.post('/api/nfts/atualizar', { acao: 'catalogo' })
    toast.success('Atualização iniciada', 'O crivonft busca os preços e recalcula pelas regras atuais. Pode levar alguns minutos.')
    await run()
  } catch (e) {
    toast.error('Não foi possível atualizar', e instanceof ApiError ? e.message : undefined)
  } finally {
    starting.value = false
  }
}

// ── regras de preço (versão e envio ao crivonft) ────────────────────────────
const envio = computed(() => regras.data.value?.ultimoEnvio)
const canResend = computed(() => ['pendente', 'falhou'].includes(envio.value?.estado ?? ''))
const resending = ref(false)
async function reenviar() {
  resending.value = true
  try {
    await api.post('/api/precificacao/reenviar')
    toast.success('Envio na fila', 'O Worker entrega as regras ao crivonft em instantes.')
    await regras.run()
  } catch (e) {
    toast.error('Não foi possível enviar', e instanceof ApiError ? e.message : undefined)
  } finally {
    resending.value = false
  }
}

const columns: Column[] = [
  { key: 'nome', label: 'NFT' },
  { key: 'custoUsd', label: 'Custo', align: 'right', hideBelow: 'md' },
  { key: 'precoVendaBrl', label: 'Preço PIX', align: 'right' },
  { key: 'variacao', label: 'Antes', align: 'right', hideBelow: 'sm' },
  { key: 'quantidade', label: 'Ofertas', align: 'right', hideBelow: 'lg' },
]

// ── coleções ────────────────────────────────────────────────────────────────
const colecoes = useAsync((signal) => api.get<{ colecoes: NftColecao[] }>('/api/nfts/colecoes', undefined, signal))
watch(tab, (t) => t === 'colecoes' && !colecoes.data.value && void colecoes.run())
const newOpen = ref(false)
const rotuloCategoria = (c: string | null) => data.value?.categorias.find((x) => x.categoria === c)?.rotulo ?? c ?? 'Sem categoria'
const colColumns: Column[] = [
  { key: 'nome', label: 'Coleção' },
  { key: 'categoria', label: 'Categoria', hideBelow: 'sm' },
  { key: 'posicoes', label: 'Posições preenchidas', align: 'right' },
  { key: 'criadoEm', label: 'Cadastro', hideBelow: 'md' },
]
</script>

<template>
  <PageCard title="NFTs" subtitle="Catálogo e preços da loja. O crivonft busca os valores no mercado e aplica as regras daqui.">
    <template #actions>
      <UiButton variant="secondary" :loading="starting" :disabled="running" @click="atualizar"><RefreshCw class="size-4" /> Atualizar preços agora</UiButton>
      <UiButton v-if="tab === 'colecoes'" @click="newOpen = true"><Plus class="size-4" /> Nova coleção</UiButton>
    </template>

    <div class="mb-6 grid gap-4 md:grid-cols-2">
      <section class="rounded-2xl border border-line p-5" aria-labelledby="cat-title">
        <div class="flex items-center justify-between gap-3">
          <h2 id="cat-title" class="text-lg font-semibold text-ink">Catálogo</h2>
          <UiBadge v-if="data" :tone="estadoCatalogo[data.status.estado]?.tone">{{ estadoCatalogo[data.status.estado]?.label }}</UiBadge>
        </div>
        <p v-if="data" class="mt-2 text-[15px] text-muted">
          {{ formatInt(data.status.itensTotal) }} itens à venda.
          <template v-if="data.status.concluidoEm">Última atualização {{ formatDateTime(data.status.concluidoEm) }}.</template>
          {{ data.mudaram ? `${formatInt(data.mudaram)} mudaram de preço desde o último dia registrado.` : '' }}
        </p>
        <p v-if="data?.status.aviso" class="mt-2 rounded-xl bg-warning-soft px-3 py-2 text-sm text-warning">{{ data.status.aviso }}</p>
      </section>

      <section class="rounded-2xl border border-line p-5" aria-labelledby="regras-title">
        <div class="flex items-center justify-between gap-3">
          <h2 id="regras-title" class="text-lg font-semibold text-ink">Regras de preço</h2>
          <StatusBadge v-if="envio" :map="estadoEnvio" :value="envio.estado" />
        </div>
        <p class="mt-2 text-[15px] text-muted">
          <template v-if="regras.data.value">Versão {{ regras.data.value.versao }}.</template>
          <template v-if="envio">
            {{ envio.estado === 'enviado' ? 'Já aplicada no crivonft.' : envio.estado === 'falhou' ? 'O último envio falhou.' : envio.estado === 'pendente' ? 'Aguardando envio ao crivonft.' : '' }}
          </template>
          Alterar as regras em <RouterLink to="/painel/precos" class="text-ink underline">Preços e regras</RouterLink> envia a nova versão sozinha.
        </p>
        <UiButton v-if="canResend" class="mt-3" size="sm" variant="secondary" :loading="resending" @click="reenviar"><Send class="size-4" /> Enviar agora</UiButton>
      </section>
    </div>

    <UiTabs v-model="tab" :tabs="tabs" class="mb-6" />

    <template v-if="tab === 'precos'">
      <UiEmptyState v-if="error && !data" :icon="TriangleAlert" title="Não foi possível carregar o catálogo" :description="error.message">
        <UiButton variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
      </UiEmptyState>
      <template v-else>
        <div class="mb-4 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <UiChips v-model="categoria" :options="categoriaOptions" label="Categoria" compact />
          <div class="flex flex-wrap items-center gap-3">
            <UiChips v-model="filtro" :options="[{ value: 'todos', label: 'Todos' }, { value: 'mudaram', label: 'Só os que mudaram' }]" label="Filtro" compact />
            <UiSearch v-model="busca" placeholder="Buscar NFT" />
          </div>
        </div>
        <DataTable :columns="columns" :rows="data?.itens" :loading="loading" :row-key="(i: NftItem) => i.categoria + i.nome" caption="NFTs do catálogo">
          <template #cell-nome="{ row }">
            <div class="flex items-center gap-3">
              <img v-if="row.imagemUrl" :src="row.imagemUrl" :alt="row.nome" class="size-10 shrink-0 rounded-lg bg-surface-3 object-contain" loading="lazy" />
              <div v-else class="size-10 shrink-0 rounded-lg bg-surface-3" />
              <div class="min-w-0">
                <p class="truncate font-medium text-ink">{{ row.nome }}</p>
                <p class="truncate text-sm text-muted">{{ rotuloCategoria(row.categoria) }}{{ row.raridade ? ` · ${row.raridade}` : '' }}</p>
              </div>
            </div>
          </template>
          <template #cell-custoUsd="{ row }"><span class="tabular">{{ formatUsd(row.custoUsd) }}</span></template>
          <template #cell-precoVendaBrl="{ row }">
            <span class="font-medium text-ink tabular">{{ formatMoney(row.precoVendaBrl) }}</span>
            <span v-if="row.variacaoPercent" :class="['ml-2 inline-flex items-center text-sm tabular', row.variacaoPercent > 0 ? 'text-danger' : 'text-success']">
              <ArrowUp v-if="row.variacaoPercent > 0" class="size-3.5" /><ArrowDown v-else class="size-3.5" />{{ formatPercent(Math.abs(row.variacaoPercent)) }}
            </span>
          </template>
          <template #cell-variacao="{ row }">
            <span v-if="row.precoAnteriorBrl !== null" class="text-muted tabular">{{ formatMoney(row.precoAnteriorBrl) }}</span>
            <span v-else class="text-faint">—</span>
          </template>
          <template #cell-quantidade="{ row }"><span class="tabular">{{ formatInt(row.quantidade) }}</span></template>
          <template #empty>
            <UiEmptyState
              :icon="Layers"
              title="Nenhum NFT encontrado"
              :description="data?.status.estado === 'nunca_executado' ? 'Clique em “Atualizar preços agora” para o crivonft montar o catálogo.' : 'Nenhum item com esses filtros.'"
            />
          </template>
          <template #footer>
            <UiPagination v-if="data" :page="data.page" :page-size="data.pageSize" :total="data.total" @update:page="(p) => (page = p)" />
          </template>
        </DataTable>
      </template>
    </template>

    <HabboPanel v-else-if="tab === 'habbo'" />

    <template v-else>
      <UiEmptyState v-if="colecoes.error.value && !colecoes.data.value" :icon="TriangleAlert" title="Não foi possível carregar as coleções" :description="colecoes.error.value.message">
        <UiButton variant="secondary" @click="colecoes.run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
      </UiEmptyState>
      <DataTable v-else :columns="colColumns" :rows="colecoes.data.value?.colecoes" :loading="colecoes.loading.value" :row-key="(c: NftColecao) => c.id" caption="Coleções">
        <template #cell-nome="{ row }"><span class="font-medium text-ink">{{ row.nome }}</span></template>
        <template #cell-categoria="{ row }">{{ rotuloCategoria(row.categoria) }}</template>
        <template #cell-posicoes="{ row }"><span class="tabular">{{ row.preenchidas }} / {{ row.totalPosicoes }}</span></template>
        <template #cell-criadoEm="{ row }">{{ formatDateTime(row.criadoEm) }}</template>
        <template #empty>
          <UiEmptyState :icon="Layers" title="Nenhuma coleção cadastrada" description="Cadastre uma coleção para acompanhar as posições e os preços dela no crivonft.">
            <UiButton @click="newOpen = true"><Plus class="size-4" /> Nova coleção</UiButton>
          </UiEmptyState>
        </template>
      </DataTable>
    </template>
  </PageCard>
  <NewCollectionModal v-model:open="newOpen" :categorias="data?.categorias ?? []" @saved="colecoes.run" />
</template>
