<script setup lang="ts">
import { Calculator, Download, RefreshCw, RotateCcw, Save, Send, TriangleAlert } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import TiersEditor from '@/components/admin/pricing/TiersEditor.vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import { ApiError, api } from '@/lib/api'
import { confirm } from '@/lib/confirm'
import { formatDateTime, formatMoney, parseDecimal } from '@/lib/format'
import { estadoEnvio, origemHistorico } from '@/lib/labels'
import { configFields, configToForm, diffPayload, fromRows, parseConfigForm, toRows, validateTiers, type ConfigForm, type TierRow } from '@/lib/pricing'
import { toast } from '@/lib/toast'
import type { CalculoPreco, HistoricoPrecificacao, Paged, Precificacao } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const tab = ref<'regras' | 'historico'>('regras')
const tabs = [
  { value: 'regras' as const, label: 'Regras' },
  { value: 'historico' as const, label: 'Histórico' },
]

const { data, loading, error, run } = useAsync((signal) => api.get<Precificacao>('/api/precificacao', undefined, signal))
void run()

// ── formulário ──────────────────────────────────────────────────────────────
const form = ref<ConfigForm>(configToForm({ valorKFeira: 0, valorKTrocas: 0, moedasPorK: 0, dolarPadraoCompra: 0, dolarPadraoVenda: 0, taxaPrata: 0, margemVendaCambiosPercent: 0 }))
const rows = ref<TierRow[]>([])
const baseline = ref('')
const snapshot = () => JSON.stringify({ f: form.value, r: rows.value.map(({ min, max, mult, ativo }) => ({ min, max, mult, ativo })) })

function fill(p: Precificacao) {
  form.value = configToForm(p.configuracao)
  rows.value = toRows(p.faixasPix)
  baseline.value = snapshot()
}
watch(data, (p) => p && fill(p))
const dirty = computed(() => !!data.value && snapshot() !== baseline.value)

const configParsed = computed(() => parseConfigForm(form.value))
const tiersValid = computed(() => validateTiers(rows.value))
const notice = ref<{ tone: 'warning' | 'danger'; text: string } | null>(null)
const showErrors = ref(false)
const configErrors = computed(() => (showErrors.value ? configParsed.value.errors : {}))

// ── salvar ──────────────────────────────────────────────────────────────────
const saving = ref(false)
async function save() {
  if (!data.value) return
  showErrors.value = true
  const values = configParsed.value.values
  if (!values || !tiersValid.value.valid) {
    notice.value = { tone: 'danger', text: 'Corrija os campos destacados antes de salvar.' }
    return
  }
  notice.value = null
  saving.value = true
  try {
    data.value = await api.put<Precificacao>('/api/precificacao', { versao: data.value.versao, configuracao: values, faixasPix: fromRows(rows.value) })
    showErrors.value = false
    toast.success('Regras salvas', `Versão ${data.value.versao}. O envio ao crivonft foi colocado na fila.`)
  } catch (e) {
    if (e instanceof ApiError && e.status === 409) {
      await run()
      notice.value = { tone: 'warning', text: 'As regras foram alteradas por outra pessoa enquanto você editava. Recarregamos a versão mais recente: revise e salve de novo.' }
    } else {
      notice.value = { tone: 'danger', text: e instanceof ApiError ? e.message : 'Não foi possível salvar.' }
    }
  } finally {
    saving.value = false
  }
}

function discard() {
  if (data.value) fill(data.value)
  showErrors.value = false
  notice.value = null
}

// ── envio e importação ──────────────────────────────────────────────────────
const sending = ref(false)
const canResend = computed(() => ['pendente', 'falhou'].includes(data.value?.ultimoEnvio?.estado ?? ''))
async function resend() {
  sending.value = true
  try {
    await api.post('/api/precificacao/reenviar')
    toast.success('Reenvio na fila', 'O Worker entrega em instantes.')
    await run()
  } catch (e) {
    toast.error('Não foi possível reenviar', e instanceof ApiError ? e.message : undefined)
  } finally {
    sending.value = false
  }
}

const importing = ref(false)
async function importFromCrivonft() {
  if (!data.value) return
  const overwrite = data.value.versao > 0
  if (!(await confirm({
    title: overwrite ? 'Sobrescrever com as regras do crivonft?' : 'Importar regras do crivonft?',
    description: overwrite
      ? 'As regras atuais daqui serão substituídas pelas que estão no crivonft. A alteração fica no histórico, mas edições não salvas serão perdidas.'
      : 'Traz as regras de preço que hoje valem no crivonft para este sistema.',
    confirmLabel: overwrite ? 'Sobrescrever' : 'Importar',
    danger: overwrite,
  }))) return
  importing.value = true
  try {
    data.value = await api.post<Precificacao>('/api/precificacao/importar-crivonft', { forcar: overwrite })
    toast.success('Regras importadas', `Agora na versão ${data.value.versao}.`)
  } catch (e) {
    toast.error('Não foi possível importar', e instanceof ApiError ? e.message : undefined)
  } finally {
    importing.value = false
  }
}

// ── calculadora ─────────────────────────────────────────────────────────────
const usd = ref('')
const usdError = ref<string | null>(null)
const calcLoading = ref(false)
const calc = ref<CalculoPreco | null>(null)
async function calculate() {
  const value = parseDecimal(usd.value)
  if (value == null || value < 0) {
    usdError.value = 'Informe um preço em USD maior ou igual a zero.'
    return
  }
  usdError.value = null
  calcLoading.value = true
  try {
    calc.value = await api.get<CalculoPreco>('/api/precificacao/calculo', { usd: value })
  } catch (e) {
    usdError.value = e instanceof ApiError ? (e.fields.usd ?? e.message) : 'Não foi possível calcular.'
  } finally {
    calcLoading.value = false
  }
}

// ── histórico ───────────────────────────────────────────────────────────────
const histPage = ref(1)
const history = useAsync((signal) => api.get<Paged<HistoricoPrecificacao>>('/api/precificacao/historico', { page: histPage.value, pageSize: 10 }, signal))
watch([tab, histPage], () => tab.value === 'historico' && void history.run())
const histColumns: Column[] = [
  { key: 'versao', label: 'Versão', class: 'w-24' },
  { key: 'quem', label: 'Quem e quando' },
  { key: 'mudancas', label: 'O que mudou', hideBelow: 'md' },
]
const envio = computed(() => data.value?.ultimoEnvio)
</script>

<template>
  <PageCard title="Preços e regras" subtitle="Como o preço PIX é calculado a partir do valor em dólar. Salvar envia as regras ao crivonft.">
    <template #actions>
      <UiButton variant="secondary" :loading="importing" :disabled="!data" @click="importFromCrivonft"><Download class="size-4" /> Importar do crivonft</UiButton>
    </template>

    <UiTabs v-model="tab" :tabs="tabs" class="mb-6" />

    <UiEmptyState v-if="error && !data" :icon="TriangleAlert" title="Não foi possível carregar as regras" :description="error.message">
      <UiButton variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
    </UiEmptyState>

    <div v-else-if="tab === 'regras' && loading && !data" class="grid gap-4 lg:grid-cols-3">
      <UiSkeleton class="h-80 lg:col-span-2" /><UiSkeleton class="h-80" />
    </div>

    <template v-else-if="tab === 'regras' && data">
      <p
        v-if="notice"
        role="alert"
        :class="['mb-4 rounded-2xl px-4 py-3 text-[15px]', notice.tone === 'danger' ? 'bg-danger-soft text-danger' : 'bg-warning-soft text-warning']"
      >{{ notice.text }}</p>

      <p v-if="data.versao === 0" class="mb-4 rounded-2xl bg-info-soft px-4 py-3 text-[15px] text-info">
        As regras ainda não foram salvas aqui. Os valores abaixo são os padrões: importe os do crivonft ou ajuste e salve.
      </p>

      <div class="grid gap-4 lg:grid-cols-3 lg:gap-6">
        <div class="space-y-4 lg:col-span-2 lg:space-y-6">
          <section class="rounded-2xl border border-line p-5" aria-labelledby="cfg-title">
            <div class="mb-4 flex items-baseline justify-between gap-3">
              <h2 id="cfg-title" class="text-lg font-semibold text-ink">Configuração de cálculo</h2>
              <span class="text-sm text-muted">Versão {{ data.versao }}</span>
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <UiInput
                v-for="f in configFields"
                :key="f.key"
                v-model="form[f.key]"
                :label="f.label"
                :hint="f.hint"
                :prefix="f.prefix"
                :suffix="f.suffix"
                :error="configErrors[f.key]"
                inputmode="decimal"
              />
            </div>
          </section>

          <section class="rounded-2xl border border-line p-5" aria-labelledby="tiers-title">
            <h2 id="tiers-title" class="text-lg font-semibold text-ink">Faixas PIX</h2>
            <p class="mt-1 mb-4 text-sm text-muted">
              O multiplicador é aplicado ao preço em dólar conforme a faixa em que ele cai. A ordem é a da lista;
            </p>
            <TiersEditor v-model="rows" />
          </section>
        </div>

        <aside class="space-y-4 lg:space-y-6">
          <section class="rounded-2xl border border-line p-5" aria-labelledby="calc-title">
            <h2 id="calc-title" class="text-lg font-semibold text-ink">Calculadora</h2>
            <p class="mt-1 mb-4 text-sm text-muted">Usa as regras salvas (versão {{ data.versao }}){{ dirty ? '; edições não salvas ficam de fora' : '' }}.</p>
            <form class="flex items-start gap-2" @submit.prevent="calculate">
              <div class="min-w-0 flex-1">
                <UiInput v-model="usd" prefix="$" placeholder="0,00" inputmode="decimal" aria-label="Preço em dólar" :error="usdError" />
              </div>
              <UiButton type="submit" size="lg" :loading="calcLoading"><Calculator class="size-4" /> Calcular</UiButton>
            </form>
            <dl v-if="calc" class="mt-5 space-y-3" aria-live="polite">
              <div>
                <dt class="text-sm text-muted">Preço PIX final</dt>
                <dd class="text-2xl font-semibold text-ink tabular" data-testid="calc-final">{{ formatMoney(calc.precoPixBrl) }}</dd>
              </div>
              <div class="flex justify-between gap-3 text-[15px]"><dt class="text-muted">Multiplicador da faixa</dt><dd class="tabular text-ink">{{ calc.multiplicadorFaixa }}×</dd></div>
              <div class="flex justify-between gap-3 text-[15px]"><dt class="text-muted">Preço pela faixa</dt><dd class="tabular text-ink">{{ formatMoney(calc.precoPelaFaixaBrl) }}</dd></div>
            </dl>
          </section>

          <section class="rounded-2xl border border-line p-5" aria-labelledby="send-title">
            <h2 id="send-title" class="mb-3 text-lg font-semibold text-ink">Envio ao crivonft</h2>
            <p v-if="!data.envioConfigurado" class="mb-3 rounded-xl bg-warning-soft px-3 py-2 text-[13px] text-warning">
              O envio não está configurado neste ambiente (BaseUrl, Token e HmacSecret). As regras são salvas, mas não seguem para o crivonft.
            </p>
            <template v-if="envio">
              <div class="flex flex-wrap items-center gap-2">
                <StatusBadge :map="estadoEnvio" :value="envio.estado" />
                <span class="text-sm text-muted">versão {{ envio.versao }}</span>
              </div>
              <dl class="mt-3 space-y-1.5 text-[14px]">
                <div v-if="envio.enviadoEm" class="flex justify-between gap-3"><dt class="text-muted">Enviado em</dt><dd class="text-right text-ink">{{ formatDateTime(envio.enviadoEm) }}</dd></div>
                <div class="flex justify-between gap-3"><dt class="text-muted">Tentativas</dt><dd class="text-ink tabular">{{ envio.tentativas }}</dd></div>
                <div v-if="envio.ultimaTentativaEm" class="flex justify-between gap-3"><dt class="text-muted">Última tentativa</dt><dd class="text-right text-ink">{{ formatDateTime(envio.ultimaTentativaEm) }}</dd></div>
                <div v-if="envio.proximaTentativaEm" class="flex justify-between gap-3"><dt class="text-muted">Próxima tentativa</dt><dd class="text-right text-ink">{{ formatDateTime(envio.proximaTentativaEm) }}</dd></div>
                <div v-if="envio.ultimoStatusHttp" class="flex justify-between gap-3"><dt class="text-muted">Resposta HTTP</dt><dd class="text-ink tabular">{{ envio.ultimoStatusHttp }}</dd></div>
              </dl>
              <p v-if="envio.ultimoErro" class="mt-3 rounded-xl bg-danger-soft px-3 py-2 text-[13px] break-words text-danger">{{ envio.ultimoErro }}</p>
              <UiButton v-if="canResend" variant="secondary" class="mt-4" :loading="sending" @click="resend"><Send class="size-4" /> Reenviar agora</UiButton>
            </template>
            <p v-else class="text-[15px] text-muted">Nenhum envio ainda. O primeiro acontece ao salvar as regras.</p>
          </section>
        </aside>
      </div>

      <div
        v-if="dirty"
        class="sticky bottom-0 z-10 mt-6 -mx-4 flex flex-wrap items-center justify-between gap-3 border-t border-line bg-surface px-4 py-4 sm:-mx-6 sm:px-6"
        role="region"
        aria-label="Alterações não salvas"
      >
        <span class="text-[15px] text-text">Você tem alterações não salvas.</span>
        <div class="flex gap-3">
          <UiButton variant="ghost" :disabled="saving" @click="discard"><RotateCcw class="size-4" /> Descartar</UiButton>
          <UiButton :loading="saving" @click="save"><Save class="size-4" /> Salvar regras</UiButton>
        </div>
      </div>
    </template>

    <template v-else-if="tab === 'historico'">
      <UiEmptyState v-if="history.error.value && !history.data.value" :icon="TriangleAlert" title="Não foi possível carregar o histórico" :description="history.error.value.message">
        <UiButton variant="secondary" @click="history.run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
      </UiEmptyState>
      <DataTable v-else :columns="histColumns" :rows="history.data.value?.items" :loading="history.loading.value" :row-key="(h: HistoricoPrecificacao) => h.id" caption="Histórico de alterações">
        <template #cell-versao="{ row }"><span class="font-medium text-ink tabular">v{{ row.versao }}</span></template>
        <template #cell-quem="{ row }">
          <span class="font-medium text-ink">{{ row.userNome }}</span>
          <p class="text-sm text-muted">{{ formatDateTime(row.criadoEm) }} · {{ origemHistorico[row.origem] ?? row.origem }}</p>
        </template>
        <template #cell-mudancas="{ row }">
          <ul v-if="diffPayload(row.antes, row.depois).length" class="space-y-0.5 text-sm">
            <li v-for="m in diffPayload(row.antes, row.depois)" :key="m">{{ m }}</li>
          </ul>
          <span v-else class="text-muted">Sem diferenças de valores</span>
        </template>
        <template #empty><UiEmptyState title="Nenhuma alteração ainda" description="Cada vez que as regras forem salvas ou importadas, o registro aparece aqui." /></template>
        <template #footer>
          <UiPagination v-if="history.data.value" :page="history.data.value.page" :page-size="history.data.value.pageSize" :total="history.data.value.total" @update:page="(p) => (histPage = p)" />
        </template>
      </DataTable>
    </template>
  </PageCard>
</template>
