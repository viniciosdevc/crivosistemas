<script setup lang="ts">
import { Download, Pencil, Pin, Plus, Scale, TriangleAlert, XCircle } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import AdjustModal from '@/components/admin/stock/AdjustModal.vue'
import FixedCostModal from '@/components/admin/stock/FixedCostModal.vue'
import PurchaseModal from '@/components/admin/stock/PurchaseModal.vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import UiSwitch from '@/components/ui/UiSwitch.vue'
import { ApiError, api } from '@/lib/api'
import { confirmWithReason } from '@/lib/confirm'
import { formatDateTime, formatDay, formatDecimal, formatFixed, formatMoney, formatUsd } from '@/lib/format'
import { motivoMovimento, origemLote } from '@/lib/labels'
import { toast } from '@/lib/toast'
import type { LoteEstoque, MovimentoEstoque, Paged, ResultadoImportacaoEstoque, ResumoEstoque, ResumoEstoques, TipoEstoque } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'
import { useAuthStore } from '@/stores/auth'

const props = defineProps<{ tipo: TipoEstoque }>()
const auth = useAuthStore()
const usd = computed(() => props.tipo === 'usd')

const lotesPageSize = 10
const movPageSize = 15
const showCancelled = ref(false)
const lotesPage = ref(1)
const movPage = ref(1)

const resumo = useAsync((signal) => api.get<ResumoEstoques>('/api/estoque/resumo', undefined, signal))
const lotes = useAsync((signal) =>
  api.get<Paged<LoteEstoque>>(`/api/estoque/${props.tipo}/lotes`, { incluirCancelados: showCancelled.value, page: lotesPage.value, pageSize: lotesPageSize }, signal),
)
const movimentos = useAsync((signal) => api.get<Paged<MovimentoEstoque>>(`/api/estoque/${props.tipo}/movimentos`, { page: movPage.value, pageSize: movPageSize }, signal))

const current = computed<ResumoEstoque | null>(() => resumo.data.value?.[usd.value ? 'usd' : 'esmeraldas'] ?? null)

watch(() => props.tipo, () => { lotesPage.value = 1; movPage.value = 1; showCancelled.value = false })
watch([() => props.tipo, showCancelled, lotesPage], () => void lotes.run(), { immediate: true })
watch([() => props.tipo, movPage], () => void movimentos.run(), { immediate: true })
void resumo.run()
for (const a of [resumo, lotes, movimentos]) watch(a.error, (e) => e && toast.error('Não foi possível carregar o estoque', e.message))
watch(showCancelled, () => (lotesPage.value = 1))

function refresh() {
  void resumo.run()
  void lotes.run()
  void movimentos.run()
}

const money = (v: number) => (usd.value ? formatMoney(v) : formatUsd(v))
const qty = (v: number) => (usd.value ? formatUsd(v) : formatDecimal(v))
const unit = (v: number) => (usd.value ? `R$ ${formatFixed(v, 4)}` : `US$ ${formatFixed(v, 6)}`)

const stats = computed<Stat[]>(() => {
  const r = current.value
  if (!r) return []
  const fixo = usd.value && r.custoMedioFixo != null
  return [
    { label: 'Saldo', text: usd.value ? formatUsd(r.saldo) : `${formatDecimal(r.saldo)} esmeraldas`, note: `${r.lotes} ${r.lotes === 1 ? 'lote' : 'lotes'} com saldo` },
    {
      label: fixo ? 'Custo médio (fixado)' : 'Custo médio',
      text: usd.value ? `R$ ${formatFixed(r.custoMedio, 4)} / US$` : `US$ ${formatFixed(r.custoMedio, 6)} / esm.`,
      note: fixo ? `Calculado: R$ ${formatFixed(r.custoMedioCalculado, 4)}` : undefined,
    },
    usd.value ? { label: 'Valor em estoque', value: r.valorEmEstoque } : { label: 'Valor em estoque', text: formatUsd(r.valorEmEstoque) },
  ]
})

const loteColumns = computed<Column[]>(() => [
  { key: 'dataCompra', label: 'Data' },
  { key: 'quantidade', label: usd.value ? 'US$' : 'Esmeraldas', align: 'right' },
  { key: 'saldo', label: 'Saldo', align: 'right' },
  { key: 'custoTotal', label: usd.value ? 'Custo (R$)' : 'Custo (US$)', align: 'right', hideBelow: 'md' },
  { key: 'custoUnitario', label: 'Custo unitário', align: 'right', hideBelow: 'lg' },
  { key: 'origem', label: 'Origem', hideBelow: 'sm' },
  { key: 'observacao', label: 'Observação', hideBelow: 'xl' },
  { key: 'acoes', label: '', align: 'right' },
])
const movColumns: Column[] = [
  { key: 'criadoEm', label: 'Quando' },
  { key: 'motivo', label: 'Motivo' },
  { key: 'quantidade', label: 'Quantidade', align: 'right' },
  { key: 'custoUnitario', label: 'Custo unitário', align: 'right', hideBelow: 'md' },
  { key: 'usuario', label: 'Quem', hideBelow: 'lg' },
  { key: 'observacao', label: 'Observação', hideBelow: 'xl' },
]

// ── ações ───────────────────────────────────────────────────────────────────
const purchaseOpen = ref(false)
const editing = ref<LoteEstoque | null>(null)
const adjustOpen = ref(false)
const fixedOpen = ref(false)

function newPurchase() { editing.value = null; purchaseOpen.value = true }
function editPurchase(l: LoteEstoque) { editing.value = l; purchaseOpen.value = true }

async function cancelPurchase(l: LoteEstoque) {
  const motivo = await confirmWithReason({
    title: 'Cancelar esta compra?',
    description: `A compra de ${formatDay(l.dataCompra)} sai do estoque e fica no histórico como cancelada.`,
    confirmLabel: 'Cancelar compra',
    danger: true,
  })
  if (motivo === null) return
  try {
    await api.post(`/api/estoque/${props.tipo}/compras/${l.id}/cancelar`, { motivo })
    toast.success('Compra cancelada')
    refresh()
  } catch (e) {
    toast.error('Não foi possível cancelar a compra', e instanceof ApiError ? e.message : undefined)
  }
}

const importing = ref(false)
async function importFromCrivonft() {
  importing.value = true
  try {
    const r = await api.post<ResultadoImportacaoEstoque>('/api/estoque/importar-crivonft')
    const total = r.usdImportados + r.esmeraldasImportados
    toast.success(
      total > 0 ? 'Estoque importado do crivonft' : 'Nada novo para importar',
      `${r.usdImportados} lote(s) de dólar, ${r.esmeraldasImportados} de esmeraldas e ${r.ignorados} já existente(s) ignorado(s).`,
    )
    refresh()
  } catch (e) {
    toast.error('Não foi possível importar do crivonft', e instanceof ApiError ? e.message : undefined)
  } finally {
    importing.value = false
  }
}
</script>

<template>
  <div class="flex flex-col gap-8">
    <div class="flex flex-wrap items-center gap-3">
      <UiButton @click="newPurchase"><Plus class="size-4" /> Registrar compra</UiButton>
      <UiButton variant="secondary" @click="adjustOpen = true"><Scale class="size-4" /> Ajustar saldo</UiButton>
      <UiButton v-if="usd && auth.isAdmin" variant="secondary" @click="fixedOpen = true"><Pin class="size-4" /> Fixar custo médio</UiButton>
      <UiButton v-if="auth.isAdmin" variant="secondary" :loading="importing" @click="importFromCrivonft"><Download class="size-4" /> Importar do crivonft</UiButton>
    </div>

    <div v-if="resumo.error.value && !current" class="rounded-2xl border border-line">
      <UiEmptyState :icon="TriangleAlert" title="Não foi possível carregar o resumo" :description="resumo.error.value.message">
        <UiButton variant="secondary" @click="resumo.run">Tentar de novo</UiButton>
      </UiEmptyState>
    </div>
    <StatBlocks v-else :stats="stats" :loading="!current" :skeletons="3" />

    <section aria-labelledby="lots-title">
      <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 id="lots-title" class="text-lg font-semibold text-ink">Compras e lotes</h2>
        <UiSwitch v-model="showCancelled" label="Mostrar cancelados" class="items-center" />
      </div>
      <DataTable :columns="loteColumns" :rows="lotes.data.value?.items" :row-key="(l) => l.id" :loading="lotes.loading.value" caption="Compras e lotes">
        <template #cell-dataCompra="{ row }">
          <span class="font-medium text-ink">{{ formatDay(row.dataCompra) }}</span>
          <UiBadge v-if="row.canceladoEm" tone="danger" class="ml-2">Cancelado</UiBadge>
        </template>
        <template #cell-quantidade="{ row }"><span class="tabular">{{ qty(row.quantidade) }}</span></template>
        <template #cell-saldo="{ row }"><span class="tabular text-ink">{{ qty(row.saldo) }}</span></template>
        <template #cell-custoTotal="{ row }"><span class="tabular">{{ money(row.custoTotal) }}</span></template>
        <template #cell-custoUnitario="{ row }"><span class="tabular">{{ unit(row.custoUnitario) }}</span></template>
        <template #cell-origem="{ row }"><StatusBadge :map="origemLote" :value="row.origem" /></template>
        <template #cell-observacao="{ row }">
          <span class="line-clamp-2 max-w-64 text-sm text-muted">{{ row.canceladoEm && row.motivoCancelamento ? `Cancelada: ${row.motivoCancelamento}` : (row.observacao ?? '—') }}</span>
        </template>
        <template #cell-acoes="{ row }">
          <div v-if="row.editavel" class="flex justify-end gap-1">
            <UiButton variant="ghost" size="icon-sm" :aria-label="`Corrigir compra de ${formatDay(row.dataCompra)}`" title="Corrigir" @click="editPurchase(row)"><Pencil class="size-4" /></UiButton>
            <UiButton variant="ghost" size="icon-sm" :aria-label="`Cancelar compra de ${formatDay(row.dataCompra)}`" title="Cancelar" @click="cancelPurchase(row)"><XCircle class="size-4 text-danger" /></UiButton>
          </div>
        </template>
        <template #empty>
          <UiEmptyState title="Nenhuma compra ainda" description="Registre a primeira compra para começar o estoque." />
        </template>
        <template #footer>
          <UiPagination v-if="lotes.data.value" :page="lotes.data.value.page" :page-size="lotes.data.value.pageSize" :total="lotes.data.value.total" @update:page="(p) => (lotesPage = p)" />
        </template>
      </DataTable>
    </section>

    <section aria-labelledby="moves-title">
      <h2 id="moves-title" class="mb-4 text-lg font-semibold text-ink">Movimentações</h2>
      <DataTable :columns="movColumns" :rows="movimentos.data.value?.items" :row-key="(m) => m.id" :loading="movimentos.loading.value" caption="Movimentações do estoque">
        <template #cell-criadoEm="{ row }"><span class="text-ink">{{ formatDateTime(row.criadoEm) }}</span></template>
        <template #cell-motivo="{ row }">
          <div class="flex flex-wrap items-center gap-2">
            <StatusBadge :map="motivoMovimento" :value="row.motivo" />
            <RouterLink v-if="row.vendaId" :to="`/painel/vendas/${row.vendaId}`" class="text-sm font-medium text-ink hover:underline">Ver venda</RouterLink>
            <UiBadge v-if="row.estornado">estornado</UiBadge>
          </div>
        </template>
        <template #cell-quantidade="{ row }">
          <span :class="['tabular font-medium', row.quantidade < 0 ? 'text-danger' : 'text-success']">{{ row.quantidade > 0 ? '+' : '' }}{{ usd ? formatUsd(row.quantidade) : formatDecimal(row.quantidade) }}</span>
        </template>
        <template #cell-custoUnitario="{ row }"><span class="tabular">{{ unit(row.custoUnitario) }}</span></template>
        <template #cell-usuario="{ row }">{{ row.usuario ?? '—' }}</template>
        <template #cell-observacao="{ row }"><span class="line-clamp-2 max-w-64 text-sm text-muted">{{ row.observacao ?? '—' }}</span></template>
        <template #empty>
          <UiEmptyState title="Nenhuma movimentação" description="Compras, ajustes e vendas aparecem aqui." />
        </template>
        <template #footer>
          <UiPagination v-if="movimentos.data.value" :page="movimentos.data.value.page" :page-size="movimentos.data.value.pageSize" :total="movimentos.data.value.total" @update:page="(p) => (movPage = p)" />
        </template>
      </DataTable>
    </section>

    <PurchaseModal v-model:open="purchaseOpen" :tipo="tipo" :lote="editing" @saved="refresh" />
    <AdjustModal v-model:open="adjustOpen" :tipo="tipo" :resumo="current" @saved="refresh" />
    <FixedCostModal v-if="usd" v-model:open="fixedOpen" :resumo="current" @saved="refresh" />
  </div>
</template>
