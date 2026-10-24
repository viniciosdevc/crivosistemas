<script setup lang="ts">
import { RefreshCw, Star, TriangleAlert, Users, Wallet } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import CarteiraEditor from '@/components/admin/clientes/CarteiraEditor.vue'
import FavoritoEstrela from '@/components/admin/clientes/FavoritoEstrela.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiAvatar from '@/components/ui/UiAvatar.vue'
import { habboAvatarUrl } from '@/lib/habbo'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import UiSearch from '@/components/ui/UiSearch.vue'
import { api } from '@/lib/api'
import { formatDate, formatInt, formatMoney } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { ClienteLista, Paged } from '@/lib/types'
import { useUrlFilters } from '@/lib/urlFilters'
import { useAsync } from '@/lib/useAsync'

const pageSize = 20
// Padrão: quem comprou mais recentemente primeiro (ordem alfabética abria a lista com clientes antigos sem compras no período importado).
const defaults = { busca: '', ordem: 'ultimaCompra', dir: 'desc', favorito: '', carteira: '', page: '1' }
const { filters, set } = useUrlFilters(defaults)

const query = computed(() => ({
  busca: filters.value.busca,
  ordem: filters.value.ordem,
  dir: filters.value.dir,
  favorito: filters.value.favorito === '1' ? true : undefined,
  carteira: filters.value.carteira === 'com' || filters.value.carteira === 'sem' ? filters.value.carteira : undefined,
  page: Number(filters.value.page) || 1,
  pageSize,
}))
const { data, loading, error, run } = useAsync((signal) => api.get<Paged<ClienteLista>>('/api/clientes', query.value, signal))
watch(query, () => void run(), { immediate: true })
watch(error, (e) => e && toast.error('Não foi possível carregar os clientes', e.message))

// Colunas ordenáveis -> nome do parâmetro `ordem` da API.
const sortKeys: Record<string, string> = { nickname: 'nome', totalGasto: 'totalGasto', compras: 'compras', ultimaCompra: 'ultimaCompra' }
const sortColumn = computed(() => Object.entries(sortKeys).find(([, v]) => v === filters.value.ordem)?.[0])
const columns: Column[] = [
  { key: 'nickname', label: 'Cliente', sortable: true },
  { key: 'carteira', label: 'Carteira', hideBelow: 'md' },
  { key: 'totalGasto', label: 'Total gasto', align: 'right', sortable: true },
  { key: 'compras', label: 'Compras', align: 'right', sortable: true, hideBelow: 'sm' },
  { key: 'ultimaCompra', label: 'Última compra', sortable: true, hideBelow: 'md' },
]
const temFiltro = computed(() => !!(filters.value.busca || filters.value.favorito || filters.value.carteira))
// Chips de alternar: "Com carteira" e "Sem carteira" se excluem; clicar de novo desliga.
const chips = computed(() => [
  { key: 'favorito', label: 'Favoritos', icon: Star, ativo: filters.value.favorito === '1', patch: { favorito: filters.value.favorito === '1' ? '' : '1' } },
  { key: 'com', label: 'Com carteira', icon: Wallet, ativo: filters.value.carteira === 'com', patch: { carteira: filters.value.carteira === 'com' ? '' : 'com' } },
  { key: 'sem', label: 'Sem carteira', icon: Wallet, ativo: filters.value.carteira === 'sem', patch: { carteira: filters.value.carteira === 'sem' ? '' : 'sem' } },
])
// A linha da lista reflete na hora o que o servidor devolveu (favorito, carteira).
function atualizarLinha(salvo: ClienteLista) {
  if (data.value) data.value = { ...data.value, items: data.value.items.map((i) => (i.id === salvo.id ? { ...i, ...salvo } : i)) }
}
function onSort(key: string, dir: 'asc' | 'desc') {
  set({ ordem: sortKeys[key] ?? 'nome', dir })
}
</script>

<template>
  <PageCard title="Clientes" subtitle="Quem compra, quanto gastou e quando comprou pela última vez.">
    <template #actions>
      <UiSearch :model-value="filters.busca" placeholder="Buscar por nome, e-mail ou carteira" @update:model-value="(v) => set({ busca: v })" />
    </template>

    <div class="mb-4 flex flex-wrap gap-2" role="group" aria-label="Filtros">
      <button
        v-for="c in chips"
        :key="c.key"
        type="button"
        :aria-pressed="c.ativo"
        :class="[
          'inline-flex h-10 items-center gap-1.5 rounded-full border px-4 text-sm transition-colors',
          c.ativo ? 'border-sel-chip bg-brand-soft font-medium text-sel-text' : 'border-line bg-surface text-text hover:border-line-strong',
        ]"
        @click="set(c.patch)"
      >
        <component :is="c.icon" class="size-4" aria-hidden="true" />{{ c.label }}
      </button>
    </div>

    <UiEmptyState v-if="error && !data" :icon="TriangleAlert" title="Não foi possível carregar os clientes" :description="error.message">
      <UiButton variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
    </UiEmptyState>
    <DataTable
      v-else
      :columns="columns"
      :rows="data?.items"
      :loading="loading"
      :row-key="(c: ClienteLista) => c.id"
      :row-to="(c: ClienteLista) => ({ name: 'customer', params: { id: c.id } })"
      :sort="sortColumn"
      :dir="filters.dir === 'asc' ? 'asc' : 'desc'"
      caption="Clientes"
      @sort="onSort"
    >
      <template #cell-nickname="{ row }">
        <div class="flex items-center gap-2">
          <FavoritoEstrela :id="row.id" :favorito="row.favorito" :nickname="row.nickname" @changed="atualizarLinha" />
          <UiAvatar :name="row.nickname" :src="habboAvatarUrl(row.nickname)" />
          <div class="min-w-0">
            <RouterLink :to="{ name: 'customer', params: { id: row.id } }" class="block truncate font-medium text-ink hover:underline">{{ row.nickname }}</RouterLink>
            <p v-if="row.email" class="truncate text-sm text-muted">{{ row.email }}</p>
          </div>
        </div>
      </template>
      <template #cell-carteira="{ row }"><CarteiraEditor :cliente="row" @changed="atualizarLinha" /></template>
      <template #cell-totalGasto="{ row }"><span class="font-medium text-ink tabular">{{ formatMoney(row.totalGasto) }}</span></template>
      <template #cell-compras="{ row }"><span class="tabular">{{ formatInt(row.compras) }}</span></template>
      <template #cell-ultimaCompra="{ row }">{{ row.ultimaCompra ? formatDate(row.ultimaCompra) : 'Nunca comprou' }}</template>
      <template #empty>
        <UiEmptyState :icon="Users" title="Nenhum cliente encontrado" :description="temFiltro ? 'Nenhum cliente com esses filtros.' : 'Os clientes aparecem aqui quando o crivonft envia a primeira venda.'" />
      </template>
      <template #footer>
        <UiPagination v-if="data" :page="data.page" :page-size="data.pageSize" :total="data.total" @update:page="(p) => set({ page: String(p) })" />
      </template>
    </DataTable>
  </PageCard>
</template>
