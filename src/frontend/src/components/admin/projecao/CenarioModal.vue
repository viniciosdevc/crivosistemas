<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiChips from '@/components/ui/UiChips.vue'
import UiDatePicker from '@/components/ui/UiDatePicker.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import { ApiError, api } from '@/lib/api'
import { formatMoney, parseDecimal, todayIso } from '@/lib/format'
import { addDays, daysBetween } from '@/lib/period'
import { toast } from '@/lib/toast'
import type { CenarioProjecao, NovoCenario, Projecao } from '@/lib/types'

/** Cria ou edita um cenário: período base (últimos N dias ou datas), até quando projetar e ajuste de crescimento, com prévia do resultado. */
const props = defineProps<{ cenario?: CenarioProjecao | null }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [projecao: Projecao] }>()

const bases = [
  { value: '7', label: '7 dias' },
  { value: '15', label: '15 dias' },
  { value: '30', label: '30 dias' },
  { value: '60', label: '60 dias' },
  { value: '90', label: '90 dias' },
  { value: 'custom', label: 'Datas' },
]
const form = reactive({ nome: '', base: '30', baseDe: '', baseAte: '', dataFinal: '', crescimento: '0' })
const loading = ref(false)
const error = ref<string | null>(null)
const fieldErrors = reactive<Record<string, string>>({})
const preview = ref<Projecao | null>(null)
let timer: ReturnType<typeof setTimeout> | undefined

watch(open, (v) => {
  if (!v) return
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  error.value = null
  preview.value = null
  const c = props.cenario
  const ontem = addDays(todayIso(), -1)
  if (c) {
    const dias = daysBetween(c.baseDe, c.baseAte)
    const preset = c.baseAte === ontem && bases.some((b) => b.value === String(dias) && b.value !== 'custom')
    Object.assign(form, { nome: c.nome, base: preset ? String(dias) : 'custom', baseDe: c.baseDe, baseAte: c.baseAte, dataFinal: c.dataFinal, crescimento: String(c.crescimentoPercent).replace('.', ',') })
  } else {
    Object.assign(form, { nome: '', base: '30', baseDe: addDays(ontem, -29), baseAte: ontem, dataFinal: addDays(todayIso(), 29), crescimento: '0' })
  }
})

function escolherBase(v: string) {
  form.base = v
  if (v === 'custom') return
  const ontem = addDays(todayIso(), -1)
  form.baseAte = ontem
  form.baseDe = addDays(ontem, -(Number(v) - 1))
}

const crescimento = computed(() => parseDecimal(form.crescimento))
const body = computed<NovoCenario | null>(() =>
  form.nome.trim() && form.baseDe && form.baseAte && form.dataFinal && crescimento.value != null
    ? { nome: form.nome.trim(), baseDe: form.baseDe, baseAte: form.baseAte, dataFinal: form.dataFinal, crescimentoPercent: crescimento.value }
    : null,
)

// Prévia: recalcula pouco depois de a pessoa parar de mexer.
watch([body, open], () => {
  clearTimeout(timer)
  if (!open.value || !body.value) return (preview.value = null)
  const b = body.value
  timer = setTimeout(async () => {
    try {
      preview.value = await api.post<Projecao>('/api/projecoes/simular', b)
      error.value = null
    } catch (e) {
      preview.value = null
      error.value = e instanceof ApiError ? Object.values(e.fields)[0] ?? e.message : 'Não foi possível calcular a prévia.'
    }
  }, 350)
})

async function submit() {
  if (!body.value) return
  loading.value = true
  error.value = null
  Object.keys(fieldErrors).forEach((k) => delete fieldErrors[k])
  try {
    const id = props.cenario?.id
    const saved = id ? await api.put<Projecao>(`/api/projecoes/${id}`, body.value) : await api.post<Projecao>('/api/projecoes', body.value)
    toast.success(id ? 'Cenário atualizado' : 'Cenário salvo')
    open.value = false
    emit('saved', saved)
  } catch (e) {
    if (e instanceof ApiError) {
      Object.assign(fieldErrors, e.fields)
      if (Object.keys(e.fields).length === 0) error.value = e.message
    } else {
      error.value = 'Não foi possível salvar o cenário.'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" compact size="md" :title="cenario ? 'Editar cenário' : 'Novo cenário'">
    <form id="cenario-form" class="flex flex-col gap-4" novalidate @submit.prevent="submit">
      <UiInput v-model="form.nome" label="Nome" maxlength="80" placeholder="Ex.: Ritmo atual" :error="fieldErrors.nome" />
      <UiChips :model-value="form.base" :options="bases" label="Período base (média diária de vendas e lucro)" @update:model-value="escolherBase" />
      <div class="grid gap-4 sm:grid-cols-2">
        <UiDatePicker v-model="form.baseDe" label="Base de" :disabled="form.base !== 'custom'" :error="fieldErrors.baseDe" />
        <UiDatePicker v-model="form.baseAte" label="Base até" :disabled="form.base !== 'custom'" :error="fieldErrors.baseAte" />
      </div>
      <div class="grid gap-4 sm:grid-cols-2">
        <UiDatePicker v-model="form.dataFinal" label="Projetar até" :min="addDays(form.baseAte || todayIso(), 1)" :error="fieldErrors.dataFinal" />
        <UiInput v-model="form.crescimento" label="Ajuste sobre a média" suffix="%" inputmode="decimal" hint="Ex.: 10 = 10% a mais; -20 = 20% a menos." :error="fieldErrors.crescimentoPercent" />
      </div>
      <p class="min-h-10 text-sm text-muted" aria-live="polite">
        <template v-if="preview">
          Média de <span class="tabular text-ink">{{ formatMoney(preview.cenario.mediaLucroDia) }}</span> de lucro por dia →
          lucro acumulado de <span class="font-semibold tabular text-ink">{{ formatMoney(preview.resumo.lucroFinal) }}</span> e caixa de
          <span class="font-semibold tabular text-ink">{{ formatMoney(preview.resumo.caixaFinal) }}</span> em {{ preview.resumo.diasTotal }} dias.
        </template>
      </p>
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="cenario-form" size="lg" :loading="loading" :disabled="!body">{{ cenario ? 'Salvar' : 'Criar cenário' }}</UiButton>
    </template>
  </UiModal>
</template>
