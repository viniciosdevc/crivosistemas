<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { ApiError, api } from '@/lib/api'
import { formatDay, formatDecimal, formatMoney, formatUsd, parseDecimal, todayIso } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { PosicaoDiaria, SaldosSistema } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

/** Posição do dia: o que você conferiu de verdade ao lado do que o sistema calcula. Não altera saldos; só mostra a diferença. */
const sistema = useAsync((signal) => api.get<SaldosSistema>('/api/posicao/sistema', undefined, signal))
const dias = useAsync((signal) => api.get<PosicaoDiaria[]>('/api/posicao', { de: undefined, ate: undefined }, signal))
void sistema.run()
void dias.run()
watch(sistema.error, (e) => e && toast.error('Não foi possível carregar os saldos', e.message))
watch(dias.error, (e) => e && toast.error('Não foi possível carregar a posição', e.message))

const hoje = computed(() => dias.data.value?.find((d) => d.data === todayIso()) ?? null)
const form = reactive({ brl: '', usd: '', cambios: '', esmeraldas: '', observacao: '' })
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})

// Preenche com o que já foi informado hoje, para corrigir em vez de digitar tudo de novo.
watch(hoje, (p) => {
  if (!p) return
  const s = (v: number | null) => (v == null ? '' : String(v).replace('.', ','))
  Object.assign(form, { brl: s(p.brlInformado), usd: s(p.usdInformado), cambios: s(p.cambiosInformado), esmeraldas: s(p.esmeraldasInformado), observacao: p.observacao ?? '' })
}, { immediate: true })

const num = (v: string) => (v.trim() === '' ? null : parseDecimal(v))
const ready = computed(() => [form.brl, form.usd, form.cambios, form.esmeraldas].some((v) => v.trim() !== '' && parseDecimal(v) != null))

async function submit() {
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  try {
    await api.put('/api/posicao/hoje', {
      brl: num(form.brl), usd: num(form.usd), cambios: num(form.cambios), esmeraldas: num(form.esmeraldas), observacao: form.observacao.trim() || null,
    })
    toast.success('Posição de hoje registrada')
    void sistema.run()
    void dias.run()
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) error.value = e.message
    } else {
      error.value = 'Não foi possível salvar.'
    }
  } finally {
    loading.value = false
  }
}

const columns: Column[] = [
  { key: 'data', label: 'Dia' },
  { key: 'brl', label: 'Caixa (R$)', align: 'right' },
  { key: 'usd', label: 'Dólares', align: 'right' },
  { key: 'cambios', label: 'Câmbios', align: 'right' },
  { key: 'esmeraldas', label: 'Esmeraldas', align: 'right', hideBelow: 'md' },
  { key: 'total', label: 'Total operacional', align: 'right' },
]

type Ativo = 'brl' | 'usd' | 'cambios' | 'esmeraldas'
const fmt: Record<Ativo, (v: number) => string> = { brl: formatMoney, usd: formatUsd, cambios: formatDecimal, esmeraldas: formatDecimal }
const informado = (p: PosicaoDiaria, a: Ativo) => ({ brl: p.brlInformado, usd: p.usdInformado, cambios: p.cambiosInformado, esmeraldas: p.esmeraldasInformado })[a]
const diferenca = (p: PosicaoDiaria, a: Ativo) => ({ brl: p.brlDiferenca, usd: p.usdDiferenca, cambios: p.cambiosDiferenca, esmeraldas: p.esmeraldasDiferenca })[a]
const limite: Record<Ativo, number> = { brl: 0.005, usd: 0.000005, cambios: 0.005, esmeraldas: 0.000005 }
const bate = (p: PosicaoDiaria, a: Ativo) => Math.abs(diferenca(p, a) ?? 0) < limite[a]

const resumo = computed(() => {
  const s = sistema.data.value
  if (!s) return []
  return [
    { label: 'Caixa (R$)', valor: formatMoney(s.brl) },
    { label: 'Dólares', valor: formatUsd(s.usd), sub: `custo médio ${formatMoney(s.custoMedioUsd)}` },
    { label: 'Câmbios', valor: formatDecimal(s.cambios) },
    { label: 'Esmeraldas', valor: formatDecimal(s.esmeraldas) },
    { label: 'Total operacional', valor: formatMoney(s.totalOperacionalBrl), sub: 'caixa + dólares ao custo médio' },
  ]
})
</script>

<template>
  <PageCard title="Posição e saldos" subtitle="Informe o que há de verdade em caixa, dólares, câmbios e esmeraldas e veja a diferença para o que o sistema calcula. Nada é alterado: para corrigir, use “Definir saldo” na tela de origem.">
    <h2 class="mb-3 text-lg font-semibold text-ink">O sistema calcula agora</h2>
    <div v-if="!sistema.data.value" class="grid gap-4 sm:grid-cols-3 lg:grid-cols-5"><UiSkeleton v-for="i in 5" :key="i" class="h-24" /></div>
    <dl v-else class="grid gap-4 sm:grid-cols-3 lg:grid-cols-5">
      <div v-for="r in resumo" :key="r.label" class="rounded-2xl border border-line p-5">
        <dt class="text-sm text-muted">{{ r.label }}</dt>
        <dd class="mt-1 text-xl font-semibold tracking-tight text-ink tabular">{{ r.valor }}</dd>
        <p v-if="r.sub" class="mt-1 text-xs text-muted">{{ r.sub }}</p>
      </div>
    </dl>

    <h2 class="mb-3 mt-8 text-lg font-semibold text-ink">Posição de hoje</h2>
    <form class="rounded-2xl border border-line p-5" novalidate @submit.prevent="submit">
      <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <UiInput v-model="form.brl" label="Caixa em R$" optional inputmode="decimal" placeholder="0,00" :error="fieldErrors.brl" />
        <UiInput v-model="form.usd" label="Dólares (US$)" optional inputmode="decimal" placeholder="0,00" :error="fieldErrors.usd" />
        <UiInput v-model="form.cambios" label="Câmbios" optional inputmode="decimal" placeholder="0" :error="fieldErrors.cambios" />
        <UiInput v-model="form.esmeraldas" label="Esmeraldas" optional inputmode="decimal" placeholder="0" :error="fieldErrors.esmeraldas" />
      </div>
      <UiInput v-model="form.observacao" class="mt-4" label="Observação" optional :error="fieldErrors.observacao" />
      <p v-if="error" class="mt-4 rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
      <div class="mt-5 flex items-center gap-3">
        <UiButton type="submit" :loading="loading" :disabled="!ready">{{ hoje ? 'Atualizar posição de hoje' : 'Registrar posição de hoje' }}</UiButton>
        <span class="text-sm text-muted">Deixe em branco o que não conferiu.</span>
      </div>
    </form>

    <h2 class="mb-3 mt-8 text-lg font-semibold text-ink">Dia a dia</h2>
    <UiEmptyState v-if="dias.data.value && dias.data.value.length === 0" title="Nenhuma posição registrada" description="Informe a posição de hoje para começar o histórico." />
    <DataTable v-else :columns="columns" :rows="dias.data.value" :row-key="(r) => r.id" :loading="dias.loading.value">
      <template #cell-data="{ row }">
        <span class="font-medium text-ink">{{ formatDay(row.data) }}</span>
        <span v-if="row.criadoPorNome" class="block text-xs text-muted">{{ row.criadoPorNome }}</span>
      </template>
      <template v-for="a in (['brl', 'usd', 'cambios', 'esmeraldas'] as Ativo[])" :key="a" #[`cell-${a}`]="{ row }">
        <template v-if="informado(row, a) == null"><span class="text-muted">—</span></template>
        <template v-else>
          <span class="tabular text-ink">{{ fmt[a](informado(row, a)!) }}</span>
          <span v-if="bate(row, a)" class="block text-xs text-success">confere</span>
          <span v-else class="tabular block text-xs" :class="diferenca(row, a)! > 0 ? 'text-warning' : 'text-danger'">
            {{ diferenca(row, a)! > 0 ? 'sobra ' : 'falta ' }}{{ fmt[a](Math.abs(diferenca(row, a)!)) }}
          </span>
        </template>
      </template>
      <template #cell-total="{ row }">
        <span class="tabular font-semibold text-ink">{{ formatMoney(row.totalOperacionalBrl) }}</span>
      </template>
    </DataTable>
  </PageCard>
</template>
