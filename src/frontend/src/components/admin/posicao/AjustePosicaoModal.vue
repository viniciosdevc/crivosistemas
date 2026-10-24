<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import { ApiError, api } from '@/lib/api'
import { formatDecimal, formatMoney, formatUsd, parseDecimal } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { PosicaoDiaria, Patrimonio } from '@/lib/types'

/** Informa os saldos reais de hoje; o servidor lança a diferença no Financeiro, no Estoque e nos Câmbios. Campo em branco não é ajustado. */
const props = defineProps<{ patrimonio: Patrimonio; hoje: PosicaoDiaria | null }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [] }>()

const form = reactive({ cambios: '', usd: '', cotacao: '', esmeraldas: '', brl: '', observacao: '' })
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})

const txt = (v: number | null | undefined) => (v == null ? '' : String(v).replace('.', ','))
watch(open, (v) => {
  if (!v) return
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  error.value = null
  const h = props.hoje
  const cotacao = props.patrimonio.usdCustoMedio > 0 ? props.patrimonio.usdCustoMedio : props.patrimonio.usdPrecoMedioVenda
  Object.assign(form, {
    cambios: txt(h?.cambiosInformado), usd: txt(h?.usdInformado), cotacao: txt(Math.round(cotacao * 10000) / 10000),
    esmeraldas: txt(h?.esmeraldasInformado), brl: txt(h?.brlInformado), observacao: h?.observacao ?? '',
  })
})

const num = (v: string) => (v.trim() === '' ? null : parseDecimal(v))
const ready = computed(() => [form.cambios, form.usd, form.esmeraldas, form.brl].some((v) => v.trim() !== '' && parseDecimal(v) != null))

async function submit() {
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  try {
    await api.put('/api/posicao/hoje', {
      cambios: num(form.cambios), usd: num(form.usd), cotacaoUsd: num(form.cotacao), esmeraldas: num(form.esmeraldas), brl: num(form.brl),
      observacao: form.observacao.trim() || null,
    })
    toast.success('Posição ajustada')
    open.value = false
    emit('saved')
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
</script>

<template>
  <UiModal v-model:open="open" title="Ajustar posição" description="Informe os saldos reais de hoje. O sistema calcula a diferença e lança o ajuste. Campos em branco não são ajustados.">
    <form id="posicao-form" class="flex flex-col gap-5" novalidate @submit.prevent="submit">
      <section class="flex flex-col gap-3">
        <p class="flex justify-between text-sm text-muted"><span>Câmbios no sistema</span><span class="tabular text-ink">{{ formatDecimal(patrimonio.cambiosSaldo) }}</span></p>
        <UiInput v-model="form.cambios" label="Câmbios atual (real)" inputmode="decimal" placeholder="0" :error="fieldErrors.cambios" />
      </section>
      <section class="flex flex-col gap-3 border-t border-line pt-5">
        <p class="flex justify-between text-sm text-muted"><span>Dólares no sistema</span><span class="tabular text-ink">{{ formatUsd(patrimonio.usdSaldo) }}</span></p>
        <div class="grid gap-4 sm:grid-cols-2">
          <UiInput v-model="form.usd" label="Dólares atual (real)" inputmode="decimal" placeholder="0,00" :error="fieldErrors.usd" />
          <UiInput v-model="form.cotacao" label="Cotação R$/USD" inputmode="decimal" placeholder="0,0000" hint="Custo do dólar que entrar no ajuste." :error="fieldErrors.cotacaoUsd" />
        </div>
      </section>
      <section class="flex flex-col gap-3 border-t border-line pt-5">
        <p class="flex justify-between text-sm text-muted"><span>Esmeraldas no sistema</span><span class="tabular text-ink">{{ formatDecimal(patrimonio.esmeraldasSaldo) }}</span></p>
        <UiInput v-model="form.esmeraldas" label="Esmeraldas atual (real)" inputmode="decimal" placeholder="0" :error="fieldErrors.esmeraldas" />
      </section>
      <section class="flex flex-col gap-3 border-t border-line pt-5">
        <p class="flex justify-between text-sm text-muted"><span>Conta em R$ no sistema</span><span class="tabular text-ink">{{ formatMoney(patrimonio.contaBrl) }}</span></p>
        <UiInput v-model="form.brl" label="Conta em R$ atual (real)" inputmode="decimal" placeholder="0,00" :error="fieldErrors.brl" />
      </section>
      <UiInput v-model="form.observacao" label="Observação" optional :error="fieldErrors.observacao" />
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="posicao-form" size="lg" :loading="loading" :disabled="!ready">Aplicar ajuste</UiButton>
    </template>
  </UiModal>
</template>
