<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, ref, watch } from 'vue'
import RankedList, { type RankedRow } from '@/components/admin/reports/RankedList.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatBlocks, { type Stat } from '@/components/ui/StatBlocks.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { api } from '@/lib/api'
import { countryName, placeLabel, toMapPoints, totalsByCountry } from '@/lib/access'
import { formatDateTime, formatInt, pluralize } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { ResumoAcessos } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

// O globo (three.js) é pesado: só baixa quando esta tela abre.
const AccessGlobe = defineAsyncComponent(() => import('@/components/admin/acessos/AccessGlobe.vue'))

const dias = ref<'7' | '30' | '90' | '365'>('30')
const diasOptions = [
  { value: '7' as const, label: '7 dias' },
  { value: '30' as const, label: '30 dias' },
  { value: '90' as const, label: '90 dias' },
  { value: '365' as const, label: '1 ano' },
]
const country = ref<string | null>(null)

const summary = useAsync((signal) => api.get<ResumoAcessos>('/api/acessos', { dias: dias.value, pais: country.value }, signal))
onMounted(() => void summary.run())
watch(dias, () => { country.value = null; void summary.run() })
// páginas e itens acompanham o país escolhido no globo
watch(country, () => void summary.run())
watch(summary.error, (e) => e && toast.error('Não foi possível carregar os acessos', e.message))

const locais = computed(() => summary.data.value?.locais ?? [])
const countries = computed(() => totalsByCountry(locais.value))
const points = computed(() => toMapPoints(locais.value))
const empty = computed(() => !!summary.data.value && summary.data.value.totais.acessos === 0)

const stats = computed<Stat[]>(() => {
  const t = summary.data.value?.totais
  if (!t) return []
  return [
    { label: 'Acessos', value: t.acessos, count: true },
    { label: 'Com cliente conectado', value: t.logados, count: true, note: t.acessos > 0 ? `${Math.round((t.logados / t.acessos) * 100)}% dos acessos` : undefined },
    { label: 'Países', value: t.paises, count: true },
    { label: 'Cidades', value: t.cidades, count: true },
  ]
})

const placeRows = computed<RankedRow[]>(() => {
  const rows = country.value
    ? locais.value.filter((l) => l.pais === country.value).map((l) => ({ key: `${l.regiao}|${l.cidade}`, label: [l.cidade, l.regiao].filter(Boolean).join(', ') || 'Sem cidade informada', acessos: l.acessos }))
    : countries.value.map((c) => ({ key: c.pais, label: c.nome, acessos: c.acessos }))
  const max = Math.max(1, ...rows.map((r) => r.acessos))
  return rows.slice(0, 15).map((r) => ({ key: r.key, label: r.label, value: formatInt(r.acessos), share: (r.acessos / max) * 100 }))
})
const categoryLabels: Record<string, string> = { clothes: 'Roupas', furni: 'Furnis', addons: 'Add-ons', pets: 'Pets' }
const pageLabels: Record<string, string> = { '/': 'Início', '/buscar': 'Busca', '/checkout': 'Checkout', '/ranking': 'Ranking', '/lootbox': 'Caixas', '/fidelidade': 'Fidelidade', '/minha-carteira': 'Minha carteira', '/minha-conta': 'Minha conta', '/como-funciona': 'Como funciona', '/entrar': 'Entrar', '/clientes': 'Clientes', '/estudio': 'Estúdio' }
const pageRows = computed<RankedRow[]>(() => {
  const list = summary.data.value?.paginas ?? []
  const max = Math.max(1, ...list.map((p) => p.visualizacoes))
  return list.slice(0, 10).map((p) => {
    const [, cat, item] = p.rota.split('/')
    const label = item ? `Itens de ${categoryLabels[cat ?? ''] ?? cat}` : cat && !pageLabels[p.rota] ? (categoryLabels[cat] ?? p.rota) : (pageLabels[p.rota] ?? p.rota)
    return { key: p.rota, label, value: formatInt(p.visualizacoes), share: (p.visualizacoes / max) * 100 }
  })
})
const items = computed(() => summary.data.value?.itens ?? [])
const clientes = computed(() => summary.data.value?.clientes ?? [])
</script>

<template>
  <PageCard title="Mapa de acessos" subtitle="De onde vêm as visitas à loja. Guardamos só país, estado e cidade — nunca o endereço IP.">
    <div class="mb-6"><UiChips v-model="dias" :options="diasOptions" label="Período" /></div>

    <UiSkeleton v-if="summary.loading.value && !summary.data.value" class="h-[420px] w-full" />
    <UiEmptyState
      v-else-if="empty"
      title="Ainda sem acessos"
      description="Os acessos começam a aparecer depois que a loja for publicada com o coletor. A localização depende dos cabeçalhos de localização da Cloudflare."
    />
    <template v-else-if="summary.data.value">
      <StatBlocks :stats="stats" class="mb-8" />

      <div class="grid gap-8 lg:grid-cols-[minmax(0,1fr)_320px]">
        <AccessGlobe :countries="countries" :points="points" :focus="country" @pick="country = $event" />
        <div>
          <div class="mb-3 flex items-center justify-between gap-2">
            <h3 class="text-lg font-semibold text-ink">{{ country ? countryName(country) : 'Países' }}</h3>
            <button v-if="country" type="button" class="text-sm text-muted underline-offset-2 hover:underline" @click="country = null">Ver todos</button>
          </div>
          <RankedList :rows="placeRows" :label="country ? 'Cidades' : 'Países'" />
          <p class="mt-3 text-[13px] text-muted">Clique num país no globo para aproximar e ver as cidades.</p>
        </div>
      </div>

      <div class="mt-10 grid gap-8 lg:grid-cols-[minmax(0,1fr)_320px]">
        <div>
          <h3 class="mb-3 text-lg font-semibold text-ink">Itens mais vistos{{ country ? ` em ${countryName(country)}` : '' }}</h3>
          <p v-if="items.length === 0" class="text-sm text-muted">Nenhum item visto no período.</p>
          <div v-else class="overflow-x-auto">
            <table class="w-full min-w-[480px] text-left text-sm">
              <thead class="bg-surface-2 text-muted">
                <tr>
                  <th class="px-4 py-3 font-medium">Item</th>
                  <th class="px-4 py-3 text-right font-medium">Visualizações</th>
                  <th class="px-4 py-3 text-right font-medium">No carrinho</th>
                  <th class="px-4 py-3 text-right font-medium">Favoritos</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-line">
                <tr v-for="i in items" :key="`${i.categoria}/${i.item}`">
                  <td class="px-4 py-3 font-medium text-ink">{{ i.item }} <span class="text-xs font-normal text-muted">{{ categoryLabels[i.categoria] ?? i.categoria }}</span></td>
                  <td class="px-4 py-3 text-right tabular">{{ formatInt(i.visualizacoes) }}</td>
                  <td class="px-4 py-3 text-right tabular">{{ formatInt(i.carrinho) }}</td>
                  <td class="px-4 py-3 text-right tabular">{{ formatInt(i.desejo) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
        <div>
          <h3 class="mb-3 text-lg font-semibold text-ink">Páginas mais acessadas</h3>
          <RankedList :rows="pageRows" label="Páginas" />
        </div>
      </div>

      <h3 class="mb-3 mt-10 text-lg font-semibold text-ink">Clientes mais acessados</h3>
      <p v-if="clientes.length === 0" class="text-sm text-muted">Nenhum cliente conectado visitou a loja ainda.</p>
      <div v-else class="overflow-x-auto">
        <table class="w-full min-w-[640px] text-left text-sm">
          <thead class="bg-surface-2 text-muted">
            <tr>
              <th class="px-4 py-3 font-medium">Cliente</th>
              <th class="px-4 py-3 text-right font-medium">Acessos</th>
              <th class="px-4 py-3 font-medium">Primeira entrada</th>
              <th class="px-4 py-3 font-medium">Última entrada</th>
              <th class="px-4 py-3 font-medium">Último local</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-line">
            <tr v-for="c in clientes" :key="c.nickname">
              <td class="px-4 py-3 font-medium text-ink">
                {{ c.nickname }}
                <span v-if="c.agora" class="ml-2 rounded-full bg-brand-soft px-2 py-0.5 text-xs text-sel-text">agora</span>
              </td>
              <td class="px-4 py-3 text-right tabular">{{ formatInt(c.acessos) }}</td>
              <td class="px-4 py-3 text-muted tabular">{{ formatDateTime(c.primeiraEntrada) }}</td>
              <td class="px-4 py-3 text-muted tabular">{{ formatDateTime(c.ultimaEntrada) }}</td>
              <td class="px-4 py-3 text-muted">{{ placeLabel(c) || '—' }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="mt-3 text-[13px] text-muted">{{ pluralize(clientes.length, 'cliente', 'clientes') }} no ranking (histórico completo, não só o período).</p>
    </template>
  </PageCard>
</template>
