<script setup lang="ts">
import { Undo2 } from 'lucide-vue-next'
import { ref, watch } from 'vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import { ApiError, api } from '@/lib/api'
import { confirmWithReason } from '@/lib/confirm'
import { formatDateTime, formatDecimal, formatMoney } from '@/lib/format'
import { tipoMovimentoCambios } from '@/lib/labels'
import { toast } from '@/lib/toast'
import type { MovimentoCambios, Paged } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

/** Livro do saldo de câmbios. Com `clienteId`, mostra só o histórico daquele cliente (sem ações). */
const props = defineProps<{ clienteId?: string }>()
const emit = defineEmits<{ changed: [] }>()
const page = ref(1)
const list = useAsync((signal) =>
  api.get<Paged<MovimentoCambios>>('/api/cambios/movimentos', { clienteId: props.clienteId, page: page.value, pageSize: 15 }, signal),
)
watch(page, () => void list.run(), { immediate: true })
watch(list.error, (e) => e && toast.error('Não foi possível carregar os câmbios', e.message))
defineExpose({ refresh: () => list.run() })

const columns: Column[] = [
  { key: 'dataHora', label: 'Quando' },
  { key: 'tipo', label: 'Tipo' },
  { key: 'quantidade', label: 'Câmbios', align: 'right' },
  { key: 'valorBrl', label: 'Valor (R$)', align: 'right', hideBelow: 'sm' },
  { key: 'saldoAnterior', label: 'Saldo anterior', align: 'right', hideBelow: 'md' },
  { key: 'saldoNovo', label: 'Saldo novo', align: 'right', hideBelow: 'md' },
  { key: 'nickname', label: 'Cliente', hideBelow: 'lg' },
  { key: 'observacao', label: 'Observação', hideBelow: 'xl' },
  ...(props.clienteId ? [] : [{ key: 'acoes', label: '', align: 'right' } as Column]),
]

async function estornar(m: MovimentoCambios) {
  const motivo = await confirmWithReason({
    title: 'Estornar este lançamento?',
    description: 'O saldo volta ao que era antes e o estorno fica registrado no histórico.',
    confirmLabel: 'Estornar',
    danger: true,
  })
  if (motivo === null) return
  try {
    await api.post(`/api/cambios/movimentos/${m.id}/estornar`, { motivo })
    toast.success('Lançamento estornado')
    await list.run()
    emit('changed')
  } catch (e) {
    toast.error('Não foi possível estornar', e instanceof ApiError ? e.message : undefined)
  }
}
</script>

<template>
  <DataTable :columns="columns" :rows="list.data.value?.items" :row-key="(m) => m.id" :loading="list.loading.value" caption="Movimentação de câmbios">
    <template #cell-dataHora="{ row }"><span class="text-ink">{{ formatDateTime(row.dataHora) }}</span></template>
    <template #cell-tipo="{ row }">
      <div class="flex flex-wrap items-center gap-2">
        <StatusBadge :map="tipoMovimentoCambios" :value="row.tipo" />
        <RouterLink v-if="row.vendaId" :to="`/painel/vendas/${row.vendaId}`" class="text-sm font-medium text-ink hover:underline">Ver venda</RouterLink>
        <UiBadge v-if="row.estornado">estornado</UiBadge>
      </div>
    </template>
    <template #cell-quantidade="{ row }">
      <span :class="['tabular font-medium', row.quantidade < 0 ? 'text-danger' : 'text-success']">{{ row.quantidade > 0 ? '+' : '' }}{{ formatDecimal(row.quantidade) }}</span>
    </template>
    <template #cell-valorBrl="{ row }"><span class="tabular">{{ row.valorBrl != null ? formatMoney(row.valorBrl) : '—' }}</span></template>
    <template #cell-saldoAnterior="{ row }"><span class="tabular">{{ formatDecimal(row.saldoAnterior) }}</span></template>
    <template #cell-saldoNovo="{ row }"><span class="tabular text-ink">{{ formatDecimal(row.saldoNovo) }}</span></template>
    <template #cell-nickname="{ row }">
      <RouterLink v-if="row.clienteId" :to="`/painel/clientes/${row.clienteId}`" class="font-medium text-ink hover:underline">{{ row.nickname }}</RouterLink>
      <span v-else>—</span>
    </template>
    <template #cell-observacao="{ row }"><span class="line-clamp-2 max-w-64 text-sm text-muted">{{ row.observacao ?? '—' }}</span></template>
    <template #cell-acoes="{ row }">
      <UiButton v-if="(row.tipo === 'venda' || row.tipo === 'ajuste') && !row.estornado" variant="ghost" size="icon-sm" aria-label="Estornar" title="Estornar" @click="estornar(row)">
        <Undo2 class="size-4" />
      </UiButton>
    </template>
    <template #empty><UiEmptyState title="Nenhum lançamento" description="Vendas de câmbios, ajustes e câmbios recebidos aparecem aqui." /></template>
    <template #footer>
      <UiPagination v-if="list.data.value" :page="list.data.value.page" :page-size="list.data.value.pageSize" :total="list.data.value.total" @update:page="(p) => (page = p)" />
    </template>
  </DataTable>
</template>
