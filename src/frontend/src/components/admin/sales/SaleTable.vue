<script setup lang="ts">
/** Tabela de vendas usada na lista, no painel e no detalhe do cliente. A linha inteira abre a venda. */
import { ReceiptText, RefreshCw, TriangleAlert } from 'lucide-vue-next'
import { computed } from 'vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiAvatar from '@/components/ui/UiAvatar.vue'
import { habboAvatarUrl } from '@/lib/habbo'
import { formatDate, formatMoney, formatTime } from '@/lib/format'
import { formaPagamento, labelOf, origemVenda, statusVenda } from '@/lib/labels'
import type { VendaLista } from '@/lib/types'

const props = defineProps<{
  rows: VendaLista[] | null | undefined
  loading?: boolean
  error?: string | null
  sort?: string
  dir?: 'asc' | 'desc'
  skeletonRows?: number
  /** Sem a coluna de cliente (detalhe do cliente) e com menos colunas (painel). */
  hideCustomer?: boolean
  compact?: boolean
  emptyTitle?: string
  emptyDescription?: string
}>()
const emit = defineEmits<{ sort: [key: string, dir: 'asc' | 'desc']; retry: [] }>()

const columns = computed<Column[]>(() => {
  const cols: (Column | false)[] = [
    { key: 'dataHora', label: 'Data', sortable: !props.compact && !!props.sort },
    { key: 'venda', label: 'Venda' },
    !props.hideCustomer && { key: 'cliente', label: 'Cliente', hideBelow: 'md' },
    !props.compact && { key: 'formaPagamento', label: 'Pagamento', hideBelow: 'lg' },
    { key: 'status', label: 'Status', hideBelow: 'sm' },
    { key: 'total', label: 'Total', align: 'right', sortable: !props.compact && !!props.sort },
    !props.compact && { key: 'lucro', label: 'Lucro', align: 'right', sortable: !!props.sort, hideBelow: 'sm' },
  ]
  return cols.filter((c): c is Column => !!c)
})
// As colunas ordenáveis usam o nome da API (`ordem`).
const sortKeys: Record<string, string> = { dataHora: 'data', total: 'total', lucro: 'lucro' }
const reverse: Record<string, string> = { data: 'dataHora', total: 'total', lucro: 'lucro' }
const sortColumn = computed(() => (props.sort ? (reverse[props.sort] ?? props.sort) : undefined))
</script>

<template>
  <UiEmptyState v-if="error && !rows" :icon="TriangleAlert" title="Não foi possível carregar as vendas" :description="error">
    <UiButton variant="secondary" @click="emit('retry')"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
  </UiEmptyState>
  <DataTable
    v-else
    :columns="columns"
    :rows="rows"
    :loading="loading"
    :row-key="(v: VendaLista) => v.id"
    :row-to="(v: VendaLista) => ({ name: 'sale', params: { id: v.id } })"
    :sort="sortColumn"
    :dir="dir"
    :skeleton-rows="skeletonRows"
    caption="Vendas"
    @sort="(k, d) => emit('sort', sortKeys[k] ?? k, d)"
  >
    <template #cell-dataHora="{ row }">
      <span class="text-ink">{{ formatDate(row.dataHora) }}</span>
      <span class="block text-sm text-muted">{{ formatTime(row.dataHora) }}</span>
    </template>
    <template #cell-venda="{ row }">
      <div class="flex flex-col items-start gap-1.5">
        <RouterLink :to="{ name: 'sale', params: { id: row.id } }" class="font-medium text-ink hover:underline">
          {{ row.pedidoId ? `Pedido ${row.pedidoId}` : `Venda ${row.origemId.slice(0, 8)}` }}
        </RouterLink>
        <span class="flex flex-wrap items-center gap-1.5">
          <StatusBadge :map="origemVenda" :value="row.origem" />
          <UiBadge v-if="row.removida" tone="danger">Removida</UiBadge>
          <StatusBadge class="sm:hidden" :map="statusVenda" :value="row.statusNormalizado" />
        </span>
      </div>
    </template>
    <template #cell-cliente="{ row }">
      <div v-if="row.cliente" class="flex items-center gap-3">
        <UiAvatar :name="row.cliente.nickname" :src="habboAvatarUrl(row.cliente.nickname)" size="sm" />
        <div class="min-w-0">
          <RouterLink :to="{ name: 'customer', params: { id: row.cliente.id } }" class="text-ink hover:underline">{{ row.cliente.nickname }}</RouterLink>
          <p v-if="row.cliente.email" class="max-w-52 truncate text-sm text-muted">{{ row.cliente.email }}</p>
        </div>
      </div>
      <span v-else class="text-faint">Sem cliente</span>
    </template>
    <template #cell-formaPagamento="{ row }">{{ labelOf(formaPagamento, row.formaPagamento).label }}</template>
    <template #cell-status="{ row }"><StatusBadge :map="statusVenda" :value="row.statusNormalizado" /></template>
    <template #cell-total="{ row }"><span class="font-medium whitespace-nowrap text-ink tabular">{{ formatMoney(row.totalBrl) }}</span></template>
    <template #cell-lucro="{ row }"><span class="whitespace-nowrap tabular" :class="row.lucroBrl < 0 && 'text-danger'">{{ formatMoney(row.lucroBrl) }}</span></template>
    <template #empty>
      <UiEmptyState :icon="ReceiptText" :title="emptyTitle ?? 'Nenhuma venda encontrada'" :description="emptyDescription ?? 'Ajuste os filtros ou aguarde novas vendas chegarem do crivonft.'" />
    </template>
    <template #footer><slot name="footer" /></template>
  </DataTable>
</template>
