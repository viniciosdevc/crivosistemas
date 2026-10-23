<script setup lang="ts">
import { RefreshCw, TriangleAlert, Users } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
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
const defaults = { busca: '', ordem: 'ultimaCompra', dir: 'desc', page: '1' }
const { filters, set } = useUrlFilters(defaults)

const query = computed(() => ({ busca: filters.value.busca, ordem: filters.value.ordem, dir: filters.value.dir, page: Number(filters.value.page) || 1, pageSize }))
const { data, loading, error, run } = useAsync((signal) => api.get<Paged<ClienteLista>>('/api/clientes', query.value, signal))
watch(query, () => void run(), { immediate: true })
watch(error, (e) => e && toast.error('Não foi possível carregar os clientes', e.message))

// Colunas ordenáveis -> nome do parâmetro `ordem` da API.
const sortKeys: Record<string, string> = { nickname: 'nome', totalGasto: 'totalGasto', compras: 'compras', ultimaCompra: 'ultimaCompra' }
const sortColumn = computed(() => Object.entries(sortKeys).find(([, v]) => v === filters.value.ordem)?.[0])
const columns: Column[] = [
  { key: 'nickname', label: 'Cliente', sortable: true },
  { key: 'totalGasto', label: 'Total gasto', align: 'right', sortable: true },
  { key: 'compras', label: 'Compras', align: 'right', sortable: true, hideBelow: 'sm' },
  { key: 'ultimaCompra', label: 'Última compra', sortable: true, hideBelow: 'md' },
]
function onSort(key: string, dir: 'asc' | 'desc') {
  set({ ordem: sortKeys[key] ?? 'nome', dir })
}
</script>

<template>
  <PageCard title="Clientes" subtitle="Quem compra, quanto gastou e quando comprou pela última vez.">
    <template #actions>
      <UiSearch :model-value="filters.busca" placeholder="Buscar por nome ou e-mail" @update:model-value="(v) => set({ busca: v })" />
    </template>

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
        <div class="flex items-center gap-3">
          <UiAvatar :name="row.nickname" :src="habboAvatarUrl(row.nickname)" />
          <div class="min-w-0">
            <RouterLink :to="{ name: 'customer', params: { id: row.id } }" class="block truncate font-medium text-ink hover:underline">{{ row.nickname }}</RouterLink>
            <p v-if="row.email" class="truncate text-sm text-muted">{{ row.email }}</p>
          </div>
        </div>
      </template>
      <template #cell-totalGasto="{ row }"><span class="font-medium text-ink tabular">{{ formatMoney(row.totalGasto) }}</span></template>
      <template #cell-compras="{ row }"><span class="tabular">{{ formatInt(row.compras) }}</span></template>
      <template #cell-ultimaCompra="{ row }">{{ row.ultimaCompra ? formatDate(row.ultimaCompra) : 'Nunca comprou' }}</template>
      <template #empty>
        <UiEmptyState :icon="Users" title="Nenhum cliente encontrado" :description="filters.busca ? 'Nenhum cliente com essa busca.' : 'Os clientes aparecem aqui quando o crivonft envia a primeira venda.'" />
      </template>
      <template #footer>
        <UiPagination v-if="data" :page="data.page" :page-size="data.pageSize" :total="data.total" @update:page="(p) => set({ page: String(p) })" />
      </template>
    </DataTable>
  </PageCard>
</template>
