<script setup lang="ts">
import { FilterX } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import SaleTable from '@/components/admin/sales/SaleTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import UiSearch from '@/components/ui/UiSearch.vue'
import UiSelect from '@/components/ui/UiSelect.vue'
import { api } from '@/lib/api'
import { toast } from '@/lib/toast'
import type { Paged, VendaLista } from '@/lib/types'
import { useUrlFilters } from '@/lib/urlFilters'
import { useAsync } from '@/lib/useAsync'

const pageSize = 20
const defaults = { de: '', ate: '', origem: '', status: '', pagamento: '', busca: '', ordem: 'data', dir: 'desc', page: '1' }
const { filters, set, reset } = useUrlFilters(defaults)

const origemOptions = [
  { value: '', label: 'Todas as origens' },
  { value: 'site', label: 'Site' },
  { value: 'nft', label: 'NFT' },
  { value: 'cambios', label: 'Câmbios' },
]
const statusOptions = [
  { value: '', label: 'Todos os status' },
  { value: 'pendente', label: 'Pendente' },
  { value: 'pago', label: 'Pago' },
  { value: 'concluido', label: 'Concluído' },
  { value: 'cancelado', label: 'Cancelado' },
  { value: 'reembolsado', label: 'Reembolsado' },
]
const paymentOptions = [
  { value: '', label: 'Todas as formas' },
  { value: 'pix', label: 'PIX' },
  { value: 'cambios', label: 'Câmbios' },
  { value: 'hibrido', label: 'Híbrido' },
]

/** Parâmetros enviados à API a partir dos filtros da URL. Vazio é descartado pelo cliente HTTP. */
const query = computed(() => ({
  de: filters.value.de,
  ate: filters.value.ate,
  origem: filters.value.origem,
  statusNormalizado: filters.value.status,
  formaPagamento: filters.value.pagamento,
  busca: filters.value.busca,
  ordem: filters.value.ordem,
  dir: filters.value.dir,
  page: Number(filters.value.page) || 1,
  pageSize,
}))

const { data, loading, error, run } = useAsync((signal) => api.get<Paged<VendaLista>>('/api/vendas', query.value, signal))
watch(query, () => void run(), { immediate: true })
watch(error, (e) => e && toast.error('Não foi possível carregar as vendas', e.message))

const hasFilters = computed(() => ['de', 'ate', 'origem', 'status', 'pagamento', 'busca'].some((k) => filters.value[k as keyof typeof defaults] !== ''))
const rangeError = computed(() => (filters.value.de && filters.value.ate && filters.value.de > filters.value.ate ? 'A data inicial deve ser anterior à final.' : null))

function onSort(key: string, dir: 'asc' | 'desc') {
  set({ ordem: key, dir })
}
</script>

<template>
  <PageCard title="Vendas" subtitle="Vendas do site, NFT e câmbios, vindas do crivonft.">
    <template #actions>
      <UiSearch :model-value="filters.busca" placeholder="Cliente, pedido, cupom ou item" @update:model-value="(v) => set({ busca: v })" />
    </template>

    <form class="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-6" @submit.prevent>
      <UiDatePicker label="De" :model-value="filters.de" :max="filters.ate || undefined" placeholder="Qualquer data" @update:model-value="(v) => set({ de: v ?? '' })" />
      <UiDatePicker label="Até" :model-value="filters.ate" :min="filters.de || undefined" :error="rangeError" placeholder="Qualquer data" @update:model-value="(v) => set({ ate: v ?? '' })" />
      <UiSelect label="Origem" :model-value="filters.origem" :options="origemOptions" @update:model-value="(v) => set({ origem: v })" />
      <UiSelect label="Status" :model-value="filters.status" :options="statusOptions" @update:model-value="(v) => set({ status: v })" />
      <UiSelect label="Pagamento" :model-value="filters.pagamento" :options="paymentOptions" @update:model-value="(v) => set({ pagamento: v })" />
      <div class="col-span-2 flex items-end lg:col-span-1">
        <UiButton variant="ghost" :disabled="!hasFilters" class="w-full" @click="reset"><FilterX class="size-4" /> Limpar filtros</UiButton>
      </div>
    </form>

    <SaleTable
      :rows="data?.items"
      :loading="loading"
      :error="error?.message"
      :sort="filters.ordem"
      :dir="filters.dir === 'asc' ? 'asc' : 'desc'"
      :empty-description="hasFilters ? 'Nenhuma venda com esses filtros. Tente ampliar o período ou limpar os filtros.' : undefined"
      @sort="onSort"
      @retry="run"
    >
      <template #footer>
        <UiPagination v-if="data" :page="data.page" :page-size="data.pageSize" :total="data.total" @update:page="(p) => set({ page: String(p) })" />
      </template>
    </SaleTable>
  </PageCard>
</template>
