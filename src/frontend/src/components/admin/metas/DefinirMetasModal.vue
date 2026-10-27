<script setup lang="ts">
import { CopyPlus } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiModal from '@/components/ui/UiModal.vue'
import { ApiError, api } from '@/lib/api'
import { formatDay, parseDecimal } from '@/lib/format'
import { addDays } from '@/lib/period'
import { toast } from '@/lib/toast'
import type { PainelMetas, SemanaMeta } from '@/lib/types'

/** Define a meta de várias semanas de uma vez; "Repetir em todas" copia um valor para as semanas listadas. Semana em branco não é alterada. */
const props = defineProps<{ semanas: SemanaMeta[] }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [painel: PainelMetas] }>()

const MESES = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']
const valores = reactive<Record<string, string>>({})
const repetir = ref('')
const loading = ref(false)
const error = ref<string | null>(null)

watch(open, (v) => {
  if (!v) return
  error.value = null
  repetir.value = ''
  for (const k of Object.keys(valores)) delete valores[k]
  for (const s of props.semanas) valores[s.semanaInicio] = String(s.metaBrl).replace('.', ',')
})

const grupos = computed(() => {
  const mapa = new Map<string, SemanaMeta[]>()
  for (const s of props.semanas) {
    const chave = s.semanaInicio.slice(0, 7)
    mapa.set(chave, [...(mapa.get(chave) ?? []), s])
  }
  return [...mapa].map(([chave, itens]) => ({ chave, mes: MESES[Number(chave.slice(5, 7)) - 1]!, itens }))
})

const valorRepetido = computed(() => parseDecimal(repetir.value))
function aplicarATodas() {
  if (!valorRepetido.value || valorRepetido.value <= 0) return
  for (const s of props.semanas) valores[s.semanaInicio] = String(valorRepetido.value).replace('.', ',')
}

function rotulo(s: SemanaMeta) {
  return `${formatDay(s.semanaInicio).slice(0, 6)} a ${formatDay(addDays(s.semanaInicio, 6)).slice(0, 6)}`
}

/** Só vai o que foi preenchido e mudou em relação ao que já valia. */
const metas = computed(() =>
  props.semanas
    .map((s) => ({ semanaInicio: s.semanaInicio, valorBrl: parseDecimal(valores[s.semanaInicio]), antes: s }))
    .filter((m) => m.valorBrl != null && m.valorBrl > 0 && (m.valorBrl !== m.antes.metaBrl || !m.antes.definida))
    .map(({ semanaInicio, valorBrl }) => ({ semanaInicio, valorBrl: valorBrl! })),
)

async function submit() {
  if (!metas.value.length) {
    error.value = 'Nenhuma meta para salvar.'
    return
  }
  loading.value = true
  error.value = null
  try {
    const painel = await api.put<PainelMetas>('/api/metas-semanais', { metas: metas.value })
    toast.success(metas.value.length === 1 ? 'Meta salva' : `${metas.value.length} metas salvas`)
    open.value = false
    emit('saved', painel)
  } catch (e) {
    error.value = e instanceof ApiError ? (Object.values(e.fields)[0] ?? e.message) : 'Não foi possível salvar as metas.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" compact size="lg" title="Definir metas" description="Meta de lucro por semana (segunda a domingo). Só as semanas que você alterar são salvas.">
    <form id="metas-form" class="flex flex-col gap-6" novalidate @submit.prevent="submit">
      <div class="flex flex-col gap-3 rounded-2xl bg-surface-2 p-4 sm:flex-row sm:items-end">
        <UiInput v-model="repetir" class="min-w-0 flex-1" label="Repetir o mesmo valor em todas as semanas" prefix="R$" inputmode="decimal" placeholder="0,00" />
        <UiButton type="button" variant="subtle" size="lg" :disabled="!valorRepetido || valorRepetido <= 0" @click="aplicarATodas"><CopyPlus class="size-4" /> Aplicar a todas</UiButton>
      </div>
      <section v-for="g in grupos" :key="g.chave" class="flex flex-col gap-3">
        <h3 class="text-[13px] font-semibold text-ink">{{ g.mes }}</h3>
        <div class="grid grid-cols-1 gap-3 min-[460px]:grid-cols-2 sm:grid-cols-3">
          <div :class="['min-w-0 rounded-xl border p-3.5', s.atual ? 'border-sel-ring bg-brand-soft' : 'border-line']" v-for="s in g.itens" :key="s.semanaInicio">
            <div class="mb-2 flex items-center justify-between gap-2 text-[13px] text-muted">
              <span>{{ rotulo(s) }}</span>
              <UiBadge v-if="s.atual" tone="brand">atual</UiBadge>
            </div>
            <UiInput v-model="valores[s.semanaInicio]" prefix="R$" inputmode="decimal" :aria-label="`Meta da semana de ${rotulo(s)}`" />
          </div>
        </div>
      </section>
      <p v-if="error" class="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{{ error }}</p>
    </form>
    <template #footer>
      <UiButton variant="subtle" size="lg" @click="open = false">Voltar</UiButton>
      <UiButton type="submit" form="metas-form" size="lg" :loading="loading" :disabled="!metas.length">Salvar metas</UiButton>
    </template>
  </UiModal>
</template>
