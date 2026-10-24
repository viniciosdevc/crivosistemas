<script setup lang="ts">
import { ArrowLeft, RefreshCw, TriangleAlert } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import SaleTable from '@/components/admin/sales/SaleTable.vue'
import WalletAddress from '@/components/admin/WalletAddress.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiAvatar from '@/components/ui/UiAvatar.vue'
import { habboAvatarUrl } from '@/lib/habbo'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import { formatDate, formatDateTime, formatInt, formatMoney } from '@/lib/format'
import type { ClienteDetalhe } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const route = useRoute()
const { data, loading, error, run } = useAsync((signal) => api.get<ClienteDetalhe>(`/api/clientes/${route.params.id}`, undefined, signal))
watch(() => route.params.id, () => void run(), { immediate: true })

const c = computed(() => data.value?.cliente)
const ticket = computed(() => (c.value && c.value.compras > 0 ? c.value.totalGasto / c.value.compras : 0))
</script>

<template>
  <PageCard title="Cliente">
    <template #title>
      <UiButton variant="ghost" size="sm" to="/painel/clientes" class="-ml-3 mb-2"><ArrowLeft class="size-4" /> Clientes</UiButton>
      <div class="flex items-center gap-4">
        <UiAvatar v-if="c" :name="c.nickname" :src="habboAvatarUrl(c.nickname)" size="lg" />
        <div class="min-w-0">
          <h1 class="truncate text-2xl font-semibold tracking-tight text-ink lg:text-[28px]">{{ c?.nickname ?? 'Cliente' }}</h1>
          <p v-if="c?.email" class="truncate text-[15px] text-muted">{{ c.email }}</p>
          <p v-if="c?.carteira" class="mt-1 text-[15px] text-muted">Carteira <WalletAddress :address="c.carteira" full /></p>
        </div>
      </div>
    </template>

    <UiEmptyState v-if="error && !data" :icon="TriangleAlert" :title="error.status === 404 ? 'Cliente não encontrado' : 'Não foi possível carregar o cliente'" :description="error.message">
      <UiButton v-if="error.status !== 404" variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
      <UiButton v-else variant="secondary" to="/painel/clientes">Voltar aos clientes</UiButton>
    </UiEmptyState>

    <div v-else-if="loading && !data" class="space-y-4"><UiSkeleton class="h-24" /><UiSkeleton class="h-64" /></div>

    <template v-else-if="data && c">
      <div class="grid grid-cols-2 gap-3 lg:grid-cols-4 lg:gap-4">
        <div class="rounded-2xl border border-line p-4 sm:p-5">
          <p class="text-sm text-muted">Total gasto</p>
          <p class="mt-2 truncate text-xl font-semibold text-ink tabular sm:text-2xl">{{ formatMoney(c.totalGasto) }}</p>
        </div>
        <div class="rounded-2xl border border-line p-4 sm:p-5">
          <p class="text-sm text-muted">Compras</p>
          <p class="mt-2 text-xl font-semibold text-ink tabular sm:text-2xl">{{ formatInt(c.compras) }}</p>
        </div>
        <div class="rounded-2xl border border-line p-4 sm:p-5">
          <p class="text-sm text-muted">Ticket médio</p>
          <p class="mt-2 truncate text-xl font-semibold text-ink tabular sm:text-2xl">{{ formatMoney(ticket) }}</p>
        </div>
        <div class="rounded-2xl border border-line p-4 sm:p-5">
          <p class="text-sm text-muted">Última compra</p>
          <p class="mt-2 text-xl font-semibold text-ink sm:text-2xl">{{ c.ultimaCompra ? formatDate(c.ultimaCompra) : '—' }}</p>
        </div>
      </div>
      <p class="mt-3 text-[13px] text-muted">
        Cliente desde {{ formatDateTime(data.criadoEmOrigem) }} · chave {{ data.chave }}. Total, compras e última compra contam só vendas pagas ou concluídas.
      </p>

      <section class="mt-6" aria-labelledby="sales-title">
        <h2 id="sales-title" class="mb-3 text-lg font-semibold text-ink">Histórico de vendas</h2>
        <SaleTable
          :rows="data.vendas"
          hide-customer
          empty-title="Nenhuma venda deste cliente"
          empty-description="Quando este cliente comprar, as vendas aparecem aqui."
          @retry="run"
        />
      </section>
    </template>
  </PageCard>
</template>
