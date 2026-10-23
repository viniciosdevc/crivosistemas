<script setup lang="ts">
import { ArrowLeft, Ban, PackageCheck, RefreshCw, TriangleAlert } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import PageCard from '@/components/ui/PageCard.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { ApiError, api } from '@/lib/api'
import { confirm, confirmWithReason } from '@/lib/confirm'
import { formatDateTime, formatDecimal, formatFixed, formatMoney, formatPercent, formatUsd } from '@/lib/format'
import { estadoEvento, formaPagamento, labelOf, motivoMovimento, origemCusto, origemVenda, statusVenda, tipoCupom, tipoEvento } from '@/lib/labels'
import { toast } from '@/lib/toast'
import type { VendaDetalhe } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const route = useRoute()
const { data, loading, error, run } = useAsync((signal) => api.get<VendaDetalhe>(`/api/vendas/${route.params.id}`, undefined, signal))
watch(() => route.params.id, () => void run(), { immediate: true })

const venda = computed(() => data.value?.venda)
const isManual = computed(() => venda.value?.origem === 'manual')
const canDeliver = computed(() => isManual.value && venda.value?.statusNormalizado === 'pendente')
const canCancel = computed(() => isManual.value && ['pendente', 'concluido'].includes(venda.value?.statusNormalizado ?? ''))
const acting = ref<'entregar' | 'cancelar' | null>(null)

async function deliver() {
  if (!venda.value) return
  const ok = await confirm({
    title: 'Marcar como entregue?',
    description: 'A venda entra no faturamento e o dólar usado no custo sai do estoque.',
    confirmLabel: 'Marcar como entregue',
  })
  if (!ok) return
  acting.value = 'entregar'
  try {
    await api.post(`/api/vendas/${venda.value.id}/entregar`)
    toast.success('Venda entregue')
    await run()
  } catch (e) {
    toast.error('Não foi possível marcar como entregue', e instanceof ApiError ? e.message : undefined)
  } finally {
    acting.value = null
  }
}

async function cancelSale() {
  if (!venda.value) return
  const motivo = await confirmWithReason({
    title: 'Cancelar esta venda?',
    description: 'O estoque usado nela volta para os mesmos lotes. A venda fica no histórico como cancelada.',
    confirmLabel: 'Cancelar venda',
    danger: true,
  })
  if (motivo === null) return
  acting.value = 'cancelar'
  try {
    await api.post(`/api/vendas/${venda.value.id}/cancelar`, { motivo })
    toast.success('Venda cancelada')
    await run()
  } catch (e) {
    toast.error('Não foi possível cancelar a venda', e instanceof ApiError ? e.message : undefined)
  } finally {
    acting.value = null
  }
}
const title = computed(() => (venda.value ? (venda.value.pedidoId ? `Pedido ${venda.value.pedidoId}` : `Venda ${venda.value.origemId.slice(0, 8)}`) : 'Venda'))
const values = computed(() => {
  const v = data.value?.valores
  if (!v) return []
  return [
    { label: 'Total', value: formatMoney(v.totalBrl), strong: true },
    { label: 'Desconto', value: formatMoney(v.descontoBrl) },
    { label: 'Taxa', value: formatMoney(v.taxaBrl) },
    { label: 'Valor em PIX', value: formatMoney(v.valorPixBrl) },
    { label: 'Valor em câmbios', value: formatDecimal(v.valorCambios) },
    { label: 'Custo', value: formatMoney(v.custoBrl) },
    { label: 'Custo em USD', value: formatUsd(v.custoUsd) },
    { label: 'Custo em câmbios', value: formatDecimal(v.custoCambios) },
    { label: 'Lucro', value: formatMoney(v.lucroBrl), strong: true, negative: v.lucroBrl < 0 },
    { label: 'Margem', value: formatPercent(v.margemPercent), negative: v.margemPercent < 0 },
  ]
})
</script>

<template>
  <PageCard :title="title">
    <template v-if="canDeliver || canCancel" #actions>
      <UiButton v-if="canDeliver" :loading="acting === 'entregar'" :disabled="acting !== null" @click="deliver"><PackageCheck class="size-4" /> Marcar como entregue</UiButton>
      <UiButton v-if="canCancel" variant="secondary" :loading="acting === 'cancelar'" :disabled="acting !== null" @click="cancelSale"><Ban class="size-4" /> Cancelar venda</UiButton>
    </template>
    <template #title>
      <UiButton variant="ghost" size="sm" to="/painel/vendas" class="-ml-3 mb-2"><ArrowLeft class="size-4" /> Vendas</UiButton>
      <div class="flex flex-wrap items-center gap-3">
        <h1 class="text-2xl font-semibold tracking-tight text-ink lg:text-[28px]">{{ title }}</h1>
        <template v-if="venda">
          <StatusBadge :map="statusVenda" :value="venda.statusNormalizado" />
          <StatusBadge :map="origemVenda" :value="venda.origem" />
          <UiBadge v-if="venda.removida" tone="danger">Removida na origem</UiBadge>
        </template>
      </div>
      <p v-if="venda" class="mt-1 text-[15px] text-muted">{{ formatDateTime(venda.dataHora) }}</p>
    </template>

    <UiEmptyState v-if="error && !data" :icon="TriangleAlert" :title="error.status === 404 ? 'Venda não encontrada' : 'Não foi possível carregar a venda'" :description="error.message">
      <UiButton v-if="error.status !== 404" variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
      <UiButton v-else variant="secondary" to="/painel/vendas">Voltar às vendas</UiButton>
    </UiEmptyState>

    <div v-else-if="loading && !data" class="grid gap-4 lg:grid-cols-3">
      <UiSkeleton class="h-64 lg:col-span-2" /><UiSkeleton class="h-64" /><UiSkeleton class="h-48 lg:col-span-3" />
    </div>

    <div v-else-if="data && venda" class="grid gap-4 lg:grid-cols-3 lg:gap-6">
      <section class="rounded-2xl border border-line p-5 lg:col-span-2" aria-labelledby="values-title">
        <h2 id="values-title" class="mb-4 text-lg font-semibold text-ink">Valores</h2>
        <dl class="grid grid-cols-2 gap-x-6 gap-y-4 sm:grid-cols-3 xl:grid-cols-5">
          <div v-for="v in values" :key="v.label">
            <dt class="text-sm text-muted">{{ v.label }}</dt>
            <dd :class="['mt-0.5 tabular', v.strong ? 'text-lg font-semibold text-ink' : 'text-text', v.negative && 'text-danger']">{{ v.value }}</dd>
          </div>
        </dl>
      </section>

      <section class="rounded-2xl border border-line p-5" aria-labelledby="info-title">
        <h2 id="info-title" class="mb-4 text-lg font-semibold text-ink">Informações</h2>
        <dl class="space-y-3 text-[15px]">
          <div>
            <dt class="text-sm text-muted">Cliente</dt>
            <dd>
              <RouterLink v-if="venda.cliente" :to="{ name: 'customer', params: { id: venda.cliente.id } }" class="font-medium text-ink hover:underline">{{ venda.cliente.nickname }}</RouterLink>
              <span v-else class="text-faint">Sem cliente identificado</span>
              <span v-if="venda.cliente?.email" class="block text-sm text-muted">{{ venda.cliente.email }}</span>
            </dd>
          </div>
          <div>
            <dt class="text-sm text-muted">Pagamento</dt>
            <dd class="text-text">{{ labelOf(formaPagamento, venda.formaPagamento).label }}</dd>
          </div>
          <div v-if="venda.vendedor">
            <dt class="text-sm text-muted">Vendedor</dt>
            <dd class="text-text">{{ venda.vendedor }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted">Cupom</dt>
            <dd v-if="data.cupom" class="text-text">
              <span class="font-medium text-ink">{{ data.cupom.codigo }}</span>
              <span v-if="data.cupom.tipo" class="text-muted"> · {{ tipoCupom[data.cupom.tipo] ?? data.cupom.tipo }}</span>
            </dd>
            <dd v-else class="text-faint">Sem cupom</dd>
          </div>
          <div>
            <dt class="text-sm text-muted">Origem do registro</dt>
            <dd class="break-all text-text">{{ venda.origemId }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted">Atualizada na origem</dt>
            <dd class="text-text">{{ formatDateTime(data.atualizadoEmOrigem) }}</dd>
          </div>
          <div v-if="data.removidaEm">
            <dt class="text-sm text-muted">Removida em</dt>
            <dd class="text-danger">{{ formatDateTime(data.removidaEm) }}</dd>
          </div>
        </dl>
        <p v-if="!venda.faturavel" class="mt-4 rounded-xl bg-warning-soft px-3 py-2 text-[13px] text-warning">Esta venda não entra no faturamento (só pagas ou concluídas, não removidas).</p>
      </section>

      <section v-if="data.manual" class="rounded-2xl border border-line p-5 lg:col-span-3" aria-labelledby="manual-title">
        <h2 id="manual-title" class="mb-4 text-lg font-semibold text-ink">Venda manual</h2>
        <dl class="grid grid-cols-2 gap-x-6 gap-y-4 sm:grid-cols-3 xl:grid-cols-5">
          <div>
            <dt class="text-sm text-muted">De onde saiu o item</dt>
            <dd class="mt-0.5 text-text">{{ data.manual.origemCusto ? (origemCusto[data.manual.origemCusto] ?? data.manual.origemCusto) : '—' }}</dd>
          </div>
          <div v-if="data.manual.quantidadeEsmeraldas != null">
            <dt class="text-sm text-muted">Esmeraldas</dt>
            <dd class="mt-0.5 tabular text-text">{{ formatDecimal(data.manual.quantidadeEsmeraldas) }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted">Taxa de prata</dt>
            <dd class="mt-0.5 text-text">{{ data.manual.usaTaxaPrata ? 'Incluída no custo' : 'Não incluída' }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted">Dólar usado</dt>
            <dd class="mt-0.5 tabular text-text">{{ data.manual.dolarCompraUsado != null ? `R$ ${formatFixed(data.manual.dolarCompraUsado, 4)}` : '—' }}</dd>
          </div>
          <div v-if="data.manual.observacao" class="col-span-2 sm:col-span-3 xl:col-span-5">
            <dt class="text-sm text-muted">Observação</dt>
            <dd class="mt-0.5 whitespace-pre-line text-text">{{ data.manual.observacao }}</dd>
          </div>
        </dl>
        <h3 class="mt-6 mb-3 text-base font-semibold text-ink">Movimentações de estoque</h3>
        <p v-if="!data.manual.movimentos.length" class="text-muted">Nenhum estoque movimentado por esta venda ainda. O dólar só sai quando ela é marcada como entregue.</p>
        <ul v-else class="divide-y divide-line rounded-xl border border-line">
          <li v-for="(m, i) in data.manual.movimentos" :key="i" class="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 px-4 py-3">
            <div class="flex items-center gap-2">
              <StatusBadge :map="motivoMovimento" :value="m.motivo" />
              <span class="text-text">{{ m.tipo === 'usd' ? 'Dólar' : 'Esmeraldas' }}</span>
            </div>
            <div class="flex items-center gap-4 text-sm">
              <span :class="['tabular font-medium', m.quantidade < 0 ? 'text-danger' : 'text-success']">{{ m.quantidade > 0 ? '+' : '' }}{{ m.tipo === 'usd' ? formatUsd(m.quantidade) : formatDecimal(m.quantidade) }}</span>
              <span class="tabular text-muted">{{ m.tipo === 'usd' ? `R$ ${formatFixed(m.custoUnitario, 4)}` : `US$ ${formatFixed(m.custoUnitario, 6)}` }} cada</span>
              <span class="text-muted">{{ formatDateTime(m.criadoEm) }}</span>
            </div>
          </li>
        </ul>
      </section>

      <section class="overflow-hidden rounded-2xl border border-line lg:col-span-3" aria-labelledby="items-title">
        <h2 id="items-title" class="px-5 pt-5 pb-3 text-lg font-semibold text-ink">Itens</h2>
        <p v-if="!data.itens.length" class="px-5 pb-5 text-muted">Esta venda não tem itens.</p>
        <div v-else class="scrollbar-thin overflow-x-auto">
          <table class="w-full text-left text-[15px]">
            <thead class="bg-surface-2 text-sm text-ink">
              <tr>
                <th scope="col" class="h-11 px-5 font-medium">Item</th>
                <th scope="col" class="h-11 px-4 text-right font-medium">Qtd.</th>
                <th scope="col" class="h-11 px-4 text-right font-medium whitespace-nowrap">Preço (USD)</th>
                <th scope="col" class="h-11 px-5 text-right font-medium whitespace-nowrap">Preço (R$)</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="i in data.itens" :key="i.ordem" class="border-t border-line">
                <td class="px-5 py-3 text-ink">
                  {{ i.descricao }}
                  <span v-if="i.referencia" class="block text-sm text-muted">Ref. {{ i.referencia }}</span>
                </td>
                <td class="px-4 py-3 text-right tabular">{{ formatDecimal(i.quantidade) }}</td>
                <td class="px-4 py-3 text-right tabular">{{ formatUsd(i.precoUsd) }}</td>
                <td class="px-5 py-3 text-right tabular text-ink">{{ formatMoney(i.precoBrl) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section v-if="!isManual || data.eventos.length" class="rounded-2xl border border-line p-5 lg:col-span-3" aria-labelledby="events-title">
        <h2 id="events-title" class="mb-1 text-lg font-semibold text-ink">Histórico de eventos</h2>
        <p class="mb-4 text-sm text-muted">Eventos do crivonft que criaram ou alteraram esta venda, do mais recente ao mais antigo.</p>
        <p v-if="!data.eventos.length" class="text-muted">Nenhum evento registrado (a venda pode ter vindo de uma carga ou reconciliação).</p>
        <ol v-else class="space-y-4 border-l border-line pl-5">
          <li v-for="e in data.eventos" :key="e.id" class="relative">
            <span class="absolute top-1.5 -left-[25px] size-2.5 rounded-full bg-line-strong" aria-hidden="true" />
            <div class="flex flex-wrap items-center gap-2">
              <span class="font-medium text-ink">{{ tipoEvento[e.tipo] ?? e.tipo }}</span>
              <StatusBadge :map="estadoEvento" :value="e.estado" />
            </div>
            <p class="text-sm text-muted">Ocorreu em {{ formatDateTime(e.ocorridoEm) }} · recebido em {{ formatDateTime(e.recebidoEm) }}</p>
            <p v-if="e.resultado" class="text-sm text-text">{{ e.resultado }}</p>
            <p v-if="e.erro" class="text-sm text-danger">{{ e.erro }}</p>
          </li>
        </ol>
      </section>
    </div>
  </PageCard>
</template>
