<script setup lang="ts">
import { RefreshCw, TriangleAlert } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import StatusBadge from '@/components/ui/StatusBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiModal from '@/components/ui/UiModal.vue'
import UiSkeleton from '@/components/ui/UiSkeleton.vue'
import { ApiError, api } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { estadoEvento, tipoEvento } from '@/lib/labels'
import { toast } from '@/lib/toast'
import type { EventoDetalhe, EventoIntegracao } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const props = defineProps<{ eventId: string | null }>()
const emit = defineEmits<{ changed: [] }>()
const open = defineModel<boolean>('open', { default: false })

const { data, loading, error, run } = useAsync((signal) => api.get<EventoDetalhe>(`/api/integracoes/crivonft/eventos/${props.eventId}`, undefined, signal))
watch(
  () => [open.value, props.eventId] as const,
  ([isOpen, id]) => {
    if (isOpen && id) void run()
  },
  { immediate: true },
)

const ev = computed(() => data.value?.evento)
const body = computed(() => {
  const raw = data.value?.corpo ?? ''
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
})

const reprocessing = ref(false)
async function reprocess() {
  if (!props.eventId) return
  reprocessing.value = true
  try {
    const updated = await api.post<EventoIntegracao>(`/api/integracoes/crivonft/eventos/${props.eventId}/reprocessar`)
    if (data.value) data.value = { ...data.value, evento: updated }
    toast[updated.estado === 'processado' ? 'success' : 'error'](updated.estado === 'processado' ? 'Evento reprocessado' : 'O evento continua com problema', updated.erro ?? undefined)
    emit('changed')
  } catch (e) {
    toast.error('Não foi possível reprocessar', e instanceof ApiError ? e.message : undefined)
  } finally {
    reprocessing.value = false
  }
}
</script>

<template>
  <UiModal v-model:open="open" title="Evento recebido" description="Dados do evento e corpo bruto enviado pelo crivonft." size="lg">
    <UiEmptyState v-if="error && !data" :icon="TriangleAlert" title="Não foi possível carregar o evento" :description="error.message">
      <UiButton variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
    </UiEmptyState>
    <div v-else-if="loading && !data" class="space-y-3"><UiSkeleton class="h-20" /><UiSkeleton class="h-48" /></div>
    <div v-else-if="ev" class="space-y-5">
      <div class="flex flex-wrap items-center gap-2">
        <span class="font-medium text-ink">{{ tipoEvento[ev.tipo] ?? ev.tipo }}</span>
        <StatusBadge :map="estadoEvento" :value="ev.estado" />
      </div>
      <dl class="grid gap-x-6 gap-y-3 text-[15px] sm:grid-cols-2">
        <div><dt class="text-sm text-muted">Recebido em</dt><dd class="text-ink">{{ formatDateTime(ev.recebidoEm) }}</dd></div>
        <div><dt class="text-sm text-muted">Ocorreu em</dt><dd class="text-ink">{{ formatDateTime(ev.ocorridoEm) }}</dd></div>
        <div><dt class="text-sm text-muted">Processado em</dt><dd class="text-ink">{{ formatDateTime(ev.processadoEm) }}</dd></div>
        <div><dt class="text-sm text-muted">Tentativas</dt><dd class="text-ink">{{ ev.tentativas }}</dd></div>
        <div><dt class="text-sm text-muted">Origem</dt><dd class="break-all text-ink">{{ ev.origem ? `${ev.origem} · ${ev.origemId ?? ''}` : '—' }}</dd></div>
        <div><dt class="text-sm text-muted">Cliente (chave)</dt><dd class="break-all text-ink">{{ ev.clienteChave ?? '—' }}</dd></div>
        <div class="sm:col-span-2"><dt class="text-sm text-muted">Id do evento</dt><dd class="break-all text-ink">{{ ev.eventoId }}</dd></div>
      </dl>
      <p v-if="ev.resultado" class="rounded-xl bg-surface-2 px-4 py-3 text-[15px] text-text">{{ ev.resultado }}</p>
      <p v-if="ev.erro" class="rounded-xl bg-danger-soft px-4 py-3 text-[15px] break-words text-danger" role="alert">{{ ev.erro }}</p>
      <div>
        <h3 class="mb-2 text-sm font-medium text-ink">Corpo</h3>
        <pre class="scrollbar-thin max-h-80 overflow-auto rounded-xl border border-line bg-surface-2 p-4 text-[13px] leading-relaxed text-text" tabindex="0">{{ body }}</pre>
      </div>
    </div>
    <template #footer>
      <UiButton variant="ghost" @click="open = false">Fechar</UiButton>
      <UiButton v-if="ev" variant="secondary" :loading="reprocessing" @click="reprocess"><RefreshCw class="size-4" /> Reprocessar</UiButton>
    </template>
  </UiModal>
</template>
