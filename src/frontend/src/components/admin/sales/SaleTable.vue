<script setup lang="ts">
/** Tabela de vendas usada na lista, no painel e no detalhe do cliente. A linha inteira abre a venda. */
import { ArrowRight, Pencil, ReceiptText, RefreshCw, TriangleAlert } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import UiImageLightbox from '@/components/ui/UiImageLightbox.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiAvatar from '@/components/ui/UiAvatar.vue'
import { habboAvatarUrl } from '@/lib/habbo'
import { formatDate, formatInt, formatMoney, formatTime } from '@/lib/format'
import { carregarImagens, imagemDoItem, nomeBaseItem } from '@/lib/imagens'
import { statusVenda } from '@/lib/labels'
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
    { key: 'item', label: 'Item' },
    !props.hideCustomer && { key: 'cliente', label: 'Comprador', hideBelow: 'md' },
    !props.compact && { key: 'pix', label: 'PIX', align: 'right', hideBelow: 'lg' },
    !props.compact && { key: 'cambios', label: 'Câmbios', align: 'right', hideBelow: 'lg' },
    { key: 'lucro', label: 'Lucro', align: 'right', sortable: !props.compact && !!props.sort, hideBelow: 'sm' },
    { key: 'status', label: 'Status', hideBelow: 'sm' },
    { key: 'acoes', label: 'Ações', align: 'right' },
  ]
  return cols.filter((c): c is Column => !!c)
})
// As colunas ordenáveis usam o nome da API (`ordem`).
const sortKeys: Record<string, string> = { dataHora: 'data', lucro: 'lucro' }
const reverse: Record<string, string> = { data: 'dataHora', lucro: 'lucro' }
const sortColumn = computed(() => (props.sort ? (reverse[props.sort] ?? props.sort) : undefined))

// Imagens dos itens em lote, só dos nomes ainda não vistos.
watch(() => props.rows, (rows) => void carregarImagens((rows ?? []).map((r) => r.item)), { immediate: true })
// Vendas antigas sem os valores separados: pix/sem forma mostram o total em PIX.
const pixBrl = (v: VendaLista) => (v.valorPixBrl > 0 ? v.valorPixBrl : v.formaPagamento === 'pix' || !v.formaPagamento ? v.totalBrl : 0)
// R$ equivalente ao que foi cobrado em câmbios: o total menos a parte em PIX.
const cambiosBrl = (v: VendaLista) => Math.max(v.totalBrl - v.valorPixBrl, 0)
const tituloItem = (v: VendaLista) => v.item ?? `Venda ${v.origemId.slice(0, 8)}`
const nomeImagem = (v: VendaLista) => nomeBaseItem(v.item) ?? tituloItem(v)
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
    <template #cell-item="{ row }">
      <div class="flex min-w-0 items-center gap-3">
        <UiImageLightbox :src="imagemDoItem(row.item)" :alt="nomeImagem(row)" />
        <div class="min-w-0">
          <RouterLink :to="{ name: 'sale', params: { id: row.id } }" class="block max-w-64 truncate font-medium text-ink hover:underline">{{ tituloItem(row) }}</RouterLink>
          <UiBadge v-if="row.removida" tone="danger" class="mt-1">Removida</UiBadge>
          <StatusBadge class="mt-1 sm:hidden" :map="statusVenda" :value="row.statusNormalizado" />
        </div>
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
    <template #cell-pix="{ row }">
      <template v-if="pixBrl(row) > 0">
        <span class="font-medium whitespace-nowrap text-ink tabular">{{ formatMoney(pixBrl(row)) }}</span>
        <span v-if="row.formaPagamento === 'hibrido'" class="block text-xs text-muted">híbrido</span>
      </template>
      <span v-else class="text-faint">—</span>
    </template>
    <template #cell-cambios="{ row }">
      <template v-if="row.valorCambios > 0">
        <span class="font-medium whitespace-nowrap text-ink tabular">{{ formatInt(row.valorCambios) }} C</span>
        <span class="block text-xs whitespace-nowrap text-muted tabular">{{ formatMoney(cambiosBrl(row)) }}</span>
      </template>
      <span v-else-if="row.formaPagamento === 'cambios'" class="font-medium whitespace-nowrap text-ink tabular">{{ formatMoney(row.totalBrl) }}</span>
      <span v-else class="text-faint">—</span>
    </template>
    <template #cell-status="{ row }"><StatusBadge :map="statusVenda" :value="row.statusNormalizado" /></template>
    <template #cell-acoes="{ row }">
      <RouterLink :to="{ name: 'sale', params: { id: row.id } }" class="inline-flex items-center gap-1 text-sm font-medium text-ink hover:underline" :aria-label="`Ver venda ${tituloItem(row)}`">
        Ver <ArrowRight class="size-4" />
      </RouterLink>
      <RouterLink
        v-if="row.origem === 'manual' && row.statusNormalizado !== 'cancelado'"
        :to="{ name: 'sale', params: { id: row.id }, query: { editar: '1' } }"
        class="ml-3 inline-flex items-center gap-1 text-sm font-medium text-muted hover:text-ink hover:underline"
        :aria-label="`Editar venda ${tituloItem(row)}`"
      >
        <Pencil class="size-3.5" /> Editar
      </RouterLink>
    </template>
    <template #cell-lucro="{ row }"><span class="whitespace-nowrap tabular" :class="row.lucroBrl < 0 && 'text-danger'">{{ formatMoney(row.lucroBrl) }}</span></template>
    <template #empty>
      <UiEmptyState :icon="ReceiptText" :title="emptyTitle ?? 'Nenhuma venda encontrada'" :description="emptyDescription ?? 'Ajuste os filtros ou aguarde novas vendas chegarem do crivonft.'" />
    </template>
    <template #footer><slot name="footer" /></template>
  </DataTable>
</template>
