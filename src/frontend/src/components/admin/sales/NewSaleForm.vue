<script setup lang="ts">
import { computed, h, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import PaymentIcon from '@/components/ui/PaymentIcon.vue'
import UiAvatar from '@/components/ui/UiAvatar.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiCheckbox from '@/components/ui/UiCheckbox.vue'
import UiSegmented from '@/components/ui/UiSegmented.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiSelect from '@/components/ui/UiSelect.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import UiTextarea from '@/components/ui/UiTextarea.vue'
import { ApiError, api } from '@/lib/api'
import { formatDecimal, formatFixed, formatInt, formatMoney, formatPercent, parseDecimal } from '@/lib/format'
import { habboAvatarUrl } from '@/lib/habbo'
import { calcularVendaManual, montarVendaManual, taxaFeiraBrl, vendaManualValida, type EntradaVendaManual } from '@/lib/sale'
import { toast } from '@/lib/toast'
import type { FormaPagamento, OrigemCusto, RegrasPrecificacao, ResumoEstoques, VendaDetalhe, VendaLista } from '@/lib/types'

/** Formulário de venda manual. O servidor recalcula custo, lucro e margem; o resumo aqui é só uma estimativa. Carrega regras e estoque ao montar. */
const props = defineProps<{ venda?: VendaDetalhe }>()
const emit = defineEmits<{ created: [venda: VendaLista]; saved: [venda: VendaLista]; cancel: [] }>()
const editando = computed(() => !!props.venda)
/** Número em texto no formato que parseDecimal aceita (vírgula decimal pt-BR). */
const txt = (n: number | null | undefined) => (n ? String(n).replace('.', ',') : '')

const PixIcon = () => h(PaymentIcon, { kind: 'pix' })
const CambiosIcon = () => h(PaymentIcon, { kind: 'cambios' })
const formaOptions = [
  { value: 'pix' as FormaPagamento, label: 'PIX', icon: PixIcon },
  { value: 'cambios' as FormaPagamento, label: 'Câmbios', icon: CambiosIcon },
  { value: 'hibrido' as FormaPagamento, label: 'Híbrido' },
]
const origemOptions: { value: OrigemCusto; label: string }[] = [
  { value: 'estoque_usd', label: 'Estoque em USD' },
  { value: 'cambios', label: 'Comprado em Câmbios' },
  { value: 'estoque_esmeralda', label: 'Estoque de Esmeraldas' },
  { value: 'manual', label: 'Custo Manual' },
]

const regras = ref<RegrasPrecificacao | null>(null)
const estoque = ref<ResumoEstoques | null>(null)
const loadError = ref<string | null>(null)
const loadingData = ref(false)

const nomeItem = ref('')
const nickname = ref('')
const observacao = ref('')
const entregue = ref(true)
const avatarNick = ref('')
let avatarTimer: ReturnType<typeof setTimeout> | undefined
const entrada = reactive<EntradaVendaManual>({
  forma: 'cambios',
  origem: 'estoque_usd',
  custoUsd: '',
  custoCambios: '',
  quantidadeEsmeraldas: '',
  usaTaxaPrata: false,
  valorPix: '',
  valorCambios: '',
})
const submitting = ref(false)
const fieldErrors = reactive<Record<string, string>>({})
const formError = ref<string | null>(null)

async function loadData() {
  loadingData.value = true
  loadError.value = null
  try {
    ;[regras.value, estoque.value] = await Promise.all([
      api.get<RegrasPrecificacao>('/api/precificacao/regras'),
      api.get<ResumoEstoques>('/api/estoque/resumo'),
    ])
  } catch (e) {
    loadError.value = e instanceof ApiError ? e.message : 'Não foi possível carregar as regras de preço.'
  } finally {
    loadingData.value = false
  }
}

function reset() {
  nomeItem.value = ''
  nickname.value = ''
  observacao.value = ''
  entregue.value = true
  avatarNick.value = ''
  Object.assign(entrada, { forma: 'cambios', origem: 'estoque_usd', custoUsd: '', custoCambios: '', quantidadeEsmeraldas: '', usaTaxaPrata: false, valorPix: '', valorCambios: '' })
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  formError.value = null
}

watch(nickname, (v) => {
  clearTimeout(avatarTimer)
  avatarTimer = setTimeout(() => (avatarNick.value = v.trim()), 400)
})
onBeforeUnmount(() => clearTimeout(avatarTimer))
// Como na calculadora, a taxa de pratas (hoje 3 C) entra no custo; o preço dela vem do valor do K e das moedas por 1K.
const taxaCambios = computed(() => regras.value?.configuracao.taxaPrata ?? null)
const taxaBrl = computed(() => (regras.value ? taxaFeiraBrl(regras.value.configuracao) : null))

function preencher(d: VendaDetalhe) {
  const origem = d.manual?.origemCusto ?? 'manual'
  nomeItem.value = d.itens[0]?.descricao ?? ''
  nickname.value = d.venda.cliente?.nickname ?? ''
  avatarNick.value = nickname.value.trim()
  observacao.value = d.manual?.observacao ?? ''
  entregue.value = d.venda.statusNormalizado === 'concluido'
  Object.assign(entrada, {
    forma: d.venda.formaPagamento ?? 'pix',
    origem,
    custoUsd: origem === 'estoque_usd' || origem === 'manual' ? txt(d.valores.custoUsd) : '',
    custoCambios: origem === 'cambios' ? txt(d.valores.custoCambios) : '',
    quantidadeEsmeraldas: txt(d.manual?.quantidadeEsmeraldas),
    usaTaxaPrata: d.manual?.usaTaxaPrata ?? true,
    valorPix: txt(d.valores.valorPixBrl),
    valorCambios: txt(d.valores.valorCambios),
  })
}

onMounted(() => {
  reset()
  if (props.venda) {
    preencher(props.venda)
    clearTimeout(avatarTimer)
  }
  void loadData()
})

const usaEsmeralda = computed(() => entrada.origem === 'estoque_esmeralda')
const usaCambios = computed(() => entrada.origem === 'cambios')
const showPix = computed(() => entrada.forma !== 'cambios')
const showCambios = computed(() => entrada.forma !== 'pix')
const esmeraldasSaldo = computed(() => estoque.value?.esmeraldas.saldo ?? 0)
const esmeraldasCusto = computed(() => estoque.value?.esmeraldas.custoMedio ?? 0)
const usdCusto = computed(() => estoque.value?.usd.custoMedio ?? 0)

const calc = computed(() =>
  regras.value
    ? calcularVendaManual(entrada, regras.value, { usdCustoMedio: usdCusto.value, esmeraldasCustoMedio: esmeraldasCusto.value })
    : null,
)
const valid = computed(() => !!regras.value && vendaManualValida(entrada, nomeItem.value))
const esmeraldasExcedem = computed(() => usaEsmeralda.value && (parseDecimal(entrada.quantidadeEsmeraldas) ?? 0) > esmeraldasSaldo.value)

async function submit() {
  if (!valid.value) return
  submitting.value = true
  formError.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  try {
    const body = montarVendaManual(entrada, {
      dataHora: editando.value ? undefined : new Date().toISOString(),
      nickname: nickname.value,
      nomeItem: nomeItem.value,
      observacao: observacao.value,
      entregue: entregue.value,
    })
    if (props.venda) {
      const venda = await api.put<VendaLista>(`/api/vendas/${props.venda.venda.id}/manual`, body)
      toast.success('Venda atualizada')
      emit('saved', venda)
      emit('created', venda)
      return
    }
    const venda = await api.post<VendaLista>('/api/vendas/manuais', body)
    toast.success('Venda registrada', entregue.value ? 'Entregue: já entra no faturamento.' : 'Pendente até ser marcada como entregue.')
    emit('created', venda)
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) toast.error(editando.value ? 'Não foi possível salvar a venda' : 'Não foi possível registrar a venda', e.message)
      else formError.value = e.message
    } else {
      toast.error(editando.value ? 'Não foi possível salvar a venda' : 'Não foi possível registrar a venda')
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <form class="sale-form flex flex-col gap-3" novalidate @submit.prevent="submit">
    <p v-if="loadError" class="rounded-xl bg-danger-soft px-3 py-2 text-sm text-danger" role="alert">
      {{ loadError }} <button type="button" class="font-medium underline" @click="loadData">Tentar de novo</button>
    </p>

    <p v-if="editando" class="rounded-xl bg-warning-soft px-3 py-2 text-[13px] text-warning" data-testid="aviso-edicao">
      Ao salvar, estoque, câmbios e financeiro são recalculados com estes valores.
    </p>

    <section class="grid gap-3 sm:grid-cols-2" aria-label="Dados da venda">
      <h3 class="text-sm font-semibold text-ink sm:col-span-2">Dados da venda</h3>
      <UiInput v-model="nomeItem" label="Nome do item" placeholder="Nome do item vendido" autocomplete="off" :error="fieldErrors.nomeItem" />
      <div class="flex items-end gap-2">
        <div class="min-w-0 flex-1">
          <UiInput v-model="nickname" label="Nickname do comprador" placeholder="Ex: ana" autocomplete="off" optional :error="fieldErrors.nicknameComprador" />
        </div>
        <UiAvatar v-if="avatarNick" :name="avatarNick" :src="habboAvatarUrl(avatarNick)" class="mb-0.5" data-testid="nick-avatar" />
      </div>

      <h3 class="mt-1 text-sm font-semibold text-ink sm:col-span-2">Pagamento e valores</h3>
      <div class="sm:col-span-2">
        <UiSegmented v-model="entrada.forma" :options="formaOptions" label="Como pagou?" variant="full" />
        <p v-if="fieldErrors.formaPagamento" class="mt-1 text-[13px] text-danger" role="alert">{{ fieldErrors.formaPagamento }}</p>
      </div>
      <UiSelect v-model="entrada.origem" label="De onde saiu o item?" :options="origemOptions" :error="fieldErrors.origemCusto" />
      <div>
        <div v-if="usaEsmeralda" class="flex flex-col gap-1">
          <UiInput
            v-model="entrada.quantidadeEsmeraldas"
            :label="`Qtde. Esmeraldas${esmeraldasSaldo > 0 ? ` (saldo: ${formatDecimal(esmeraldasSaldo)})` : ''}`"
            inputmode="decimal"
            placeholder="0"
            :error="fieldErrors.quantidadeEsmeraldas"
          />
          <p v-if="estoque && esmeraldasSaldo <= 0" class="text-xs text-warning">Sem saldo de esmeraldas no estoque.</p>
          <p v-else-if="esmeraldasExcedem" class="text-xs text-warning">A quantidade passa do saldo disponível.</p>
          <p v-else-if="estoque && esmeraldasCusto <= 0" class="text-xs text-warning">As esmeraldas em estoque estão sem custo.</p>
          <p v-else-if="calc && calc.qtdEsmeraldas > 0" class="text-xs text-muted">≈ US$ {{ formatFixed(calc.custoUsd, 4) }}</p>
        </div>
        <UiInput v-else-if="usaCambios" v-model="entrada.custoCambios" label="Custo em Câmbios" inputmode="numeric" placeholder="0" suffix="C" :error="fieldErrors.custoCambios" />
        <div v-else class="flex flex-col gap-1">
          <UiInput v-model="entrada.custoUsd" label="Custo USD" prefix="US$" inputmode="decimal" placeholder="0,00" :error="fieldErrors.custoUsd" />
          <p v-if="entrada.origem === 'estoque_usd' && calc" class="text-xs text-muted">
            Dólar: {{ formatMoney(calc.dolarCompra) }}{{ usdCusto > 0 ? ' (custo médio)' : ' (configuração)' }}
          </p>
        </div>
      </div>

      <div v-if="showPix" :class="showCambios ? '' : 'sm:col-span-2'">
        <UiInput v-model="entrada.valorPix" label="Valor recebido em PIX" prefix="R$" inputmode="decimal" placeholder="0,00" :error="fieldErrors.valorVendaPix">
          <template #prefix><PaymentIcon kind="pix" /></template>
        </UiInput>
      </div>
      <div v-if="showCambios" :class="showPix ? '' : 'sm:col-span-2'">
        <UiInput v-model="entrada.valorCambios" label="Valor recebido em Câmbios" suffix="C" inputmode="numeric" placeholder="0" :error="fieldErrors.valorVendaCambios">
          <template #prefix><PaymentIcon kind="cambios" /></template>
        </UiInput>
      </div>
      <div class="flex items-center rounded-xl bg-surface-2 px-3 py-2 sm:col-span-2">
        <UiCheckbox v-model="entrada.usaTaxaPrata" label="Taxa da feira (pratas)" />
        <span v-if="taxaBrl !== null" class="ml-auto pl-2 text-right text-xs leading-tight whitespace-nowrap text-muted tabular" data-testid="taxa-feira">
          {{ formatInt(taxaCambios ?? 0) }} C<br />{{ formatMoney(taxaBrl) }}
        </span>
      </div>
    </section>

    <section class="rounded-2xl border border-line bg-surface-2 px-4 py-3" aria-labelledby="sale-summary-title">
      <h3 id="sale-summary-title" class="mb-2 text-sm font-semibold text-ink">Resumo do cálculo</h3>
      <UiSkeleton v-if="loadingData && !calc" class="h-12" />
      <template v-else-if="calc">
        <dl class="grid grid-cols-2 gap-x-4 gap-y-2 sm:grid-cols-4">
          <div>
            <dt class="text-xs text-muted">Custo</dt>
            <dd class="truncate text-[15px] font-semibold text-ink tabular">{{ formatMoney(calc.result.custoBrl) }}</dd>
            <dd v-if="!usaCambios" class="truncate text-xs text-muted tabular">US$ {{ formatFixed(calc.custoUsd, 2) }}</dd>
          </div>
          <div>
            <dt class="text-xs text-muted">Receita</dt>
            <dd class="truncate text-[15px] font-semibold text-ink tabular">{{ formatMoney(calc.result.receitaTotalBrl) }}</dd>
          </div>
          <div>
            <dt class="text-xs text-muted">Lucro</dt>
            <dd :class="['truncate text-[15px] font-semibold tabular', calc.result.lucroBrl < 0 ? 'text-danger' : calc.result.lucroBrl > 0 ? 'text-success' : 'text-ink']">
              {{ formatMoney(calc.result.lucroBrl) }}
            </dd>
          </div>
          <div>
            <dt class="text-xs text-muted">Margem</dt>
            <dd :class="['truncate text-[15px] font-semibold tabular', calc.abaixoDoIdeal ? 'text-warning' : 'text-success']">{{ formatPercent(calc.result.margemPercent) }}</dd>
            <dd class="truncate text-xs text-muted">ideal {{ formatPercent(calc.margemIdeal) }}</dd>
          </div>
        </dl>
        <div class="mt-2 flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-t border-line pt-2 text-xs text-muted">
          <span class="inline-flex flex-wrap items-center gap-x-1.5">
            Referência: <PaymentIcon kind="pix" class="size-3.5" /> {{ formatMoney(calc.result.precoVendaPix) }} · <PaymentIcon kind="cambios" class="size-3.5" /> {{ formatInt(calc.result.precoVendaCambios) }} C
          </span>
          <span v-if="showCambios">Resultado em câmbios: <strong class="font-semibold text-ink">{{ formatInt(calc.result.lucroCambios) }} C</strong></span>
        </div>
      </template>
    </section>

    <p v-if="formError" class="rounded-xl bg-danger-soft px-3 py-2 text-sm text-danger" role="alert">{{ formError }}</p>

    <UiTextarea v-model="observacao" label="Observação" optional placeholder="Alguma anotação sobre esta venda" :error="fieldErrors.observacao" />
    <div class="flex flex-wrap items-center justify-between gap-3">
      <UiCheckbox v-model="entregue" label="Já entregue" />
      <div class="flex items-center gap-2">
        <UiButton variant="subtle" @click="emit('cancel')">Voltar</UiButton>
        <UiButton type="submit" :loading="submitting" :disabled="!valid">{{ editando ? 'Salvar alterações' : 'Confirmar venda' }}</UiButton>
      </div>
    </div>
  </form>
</template>

<style scoped>
/* Formulário denso: controles de 40px e textarea baixo, para caber sem rolar em notebooks. */
.sale-form :deep(input:not([type='checkbox'])),
.sale-form :deep(button[class*='h-12']) {
  height: 2.5rem;
}
.sale-form :deep(textarea) {
  height: 2.5rem;
  min-height: 0;
  resize: none;
  padding-block: 0.5rem;
}
</style>
