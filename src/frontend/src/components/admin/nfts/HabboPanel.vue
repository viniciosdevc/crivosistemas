<script setup lang="ts">
import { PlugZap, RefreshCw, Send, TriangleAlert } from 'lucide-vue-next'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiSwitch from '@/components/ui/UiSwitch.vue'
import { ApiError, api } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { toast } from '@/lib/toast'
import type { HabboEnvio, HabboEstado } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

/** Integração do crivonft com a Web API do Habbo: aqui só se consulta e se dispara; as chaves e o envio ficam no crivonft. */
const { data, loading, error, run } = useAsync((signal) => api.get<HabboEstado>('/api/nfts/habbo', undefined, signal))
void run()

const estadoEnvio: Record<HabboEnvio['status'], { label: string; tone: 'info' | 'success' | 'warning' | 'danger' }> = {
  executando: { label: 'Enviando…', tone: 'info' },
  ok: { label: 'Enviado', tone: 'success' },
  parcial: { label: 'Parcial', tone: 'warning' },
  erro: { label: 'Erro', tone: 'danger' },
}
const enviando = computed(() => data.value?.envios.some((e) => e.status === 'executando') ?? false)
let timer: ReturnType<typeof setInterval> | undefined
watch(enviando, (v) => {
  clearInterval(timer)
  if (v) timer = setInterval(() => void run(), 5000)
})
onBeforeUnmount(() => clearInterval(timer))
const ultimo = computed(() => data.value?.envios.find((e) => e.status !== 'executando') ?? null)

const roomId = ref('')
watch(data, (d) => d && (roomId.value = d.config.roomId ?? ''), { once: true })

const busy = ref<'testar' | 'enviar' | 'config' | null>(null)
async function testar() {
  busy.value = 'testar'
  try {
    const r = await api.post<{ ok: boolean; mensagem: string }>('/api/nfts/habbo/testar')
    if (r.ok) toast.success('Conexão com o Habbo', r.mensagem)
    else toast.error('Conexão com o Habbo', r.mensagem)
  } catch (e) {
    toast.error('Não foi possível testar', e instanceof ApiError ? e.message : undefined)
  } finally {
    busy.value = null
  }
}
async function enviar(destinoId?: string) {
  busy.value = 'enviar'
  try {
    await api.post('/api/nfts/habbo/enviar', { destinoId: destinoId ?? null })
    toast.success('Envio iniciado', 'O crivonft grava os preços no quarto do Habbo.')
    await run()
  } catch (e) {
    toast.error('Não foi possível enviar', e instanceof ApiError ? e.message : undefined)
  } finally {
    busy.value = null
  }
}
async function salvarConfig(patch: { roomId?: string; envioAutomatico?: boolean }) {
  busy.value = 'config'
  try {
    await api.put('/api/nfts/habbo/config', patch)
    toast.success('Configuração salva no crivonft')
    await run()
  } catch (e) {
    toast.error('Não foi possível salvar', e instanceof ApiError ? e.message : undefined)
  } finally {
    busy.value = null
  }
}
</script>

<template>
  <UiEmptyState v-if="error && !data" :icon="TriangleAlert" title="Não foi possível carregar a integração com o Habbo" :description="error.message">
    <UiButton variant="secondary" @click="run"><RefreshCw class="size-4" /> Tentar de novo</UiButton>
  </UiEmptyState>
  <div v-else-if="data" class="grid gap-4 lg:grid-cols-2">
    <section class="rounded-2xl border border-line p-5" aria-labelledby="hb-conn">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 id="hb-conn" class="text-lg font-semibold text-ink">Conexão</h2>
        <div class="flex gap-2">
          <UiBadge :tone="data.chaves.leitura ? 'success' : 'danger'">Leitura {{ data.chaves.leitura ? 'ok' : 'sem chave' }}</UiBadge>
          <UiBadge :tone="data.chaves.escrita ? 'success' : 'danger'">Escrita {{ data.chaves.escrita ? 'ok' : 'sem chave' }}</UiBadge>
        </div>
      </div>
      <p class="mt-2 text-[15px] text-muted">As chaves ficam só no crivonft. Hotel: {{ data.config.hotel }}.</p>
      <form class="mt-4 flex items-start gap-2" novalidate @submit.prevent="salvarConfig({ roomId })">
        <div class="min-w-0 flex-1"><UiInput v-model="roomId" label="Número do quarto" inputmode="numeric" placeholder="Só dígitos" /></div>
        <UiButton type="submit" variant="secondary" size="lg" class="mt-[26px]" :loading="busy === 'config'">Salvar</UiButton>
      </form>
      <div class="mt-4">
        <UiSwitch
          :model-value="data.config.envioAutomatico"
          label="Envio automático"
          description="Quando uma lista de preços é gerada no crivonft, ela já vai para o quarto."
          :disabled="busy === 'config'"
          @update:model-value="(v: boolean) => salvarConfig({ envioAutomatico: v })"
        />
      </div>
      <UiButton class="mt-4" variant="secondary" :loading="busy === 'testar'" @click="testar"><PlugZap class="size-4" /> Testar conexão</UiButton>
    </section>

    <section class="rounded-2xl border border-line p-5" aria-labelledby="hb-send">
      <div class="flex items-center justify-between gap-3">
        <h2 id="hb-send" class="text-lg font-semibold text-ink">Preços no quarto</h2>
        <UiButton :loading="busy === 'enviar' || enviando" :disabled="!data.chaves.escrita || data.destinos.length === 0" @click="enviar()"><Send class="size-4" /> Enviar tudo agora</UiButton>
      </div>
      <p class="mt-2 text-[15px] text-muted">
        <template v-if="ultimo">Último envio {{ formatDateTime(ultimo.concluidoEm ?? ultimo.iniciadoEm) }} ({{ ultimo.origem === 'automatico' ? 'automático' : 'manual' }}).</template>
        <template v-else>Nenhum envio feito ainda.</template>
        Os destinos (listas e variáveis do quarto) são configurados no painel do crivonft.
      </p>
      <ul v-if="data.destinos.length" class="mt-3 divide-y divide-line rounded-xl border border-line">
        <li v-for="d in data.destinos" :key="d.id" class="flex items-center justify-between gap-3 px-4 py-3">
          <div class="min-w-0">
            <p class="truncate font-medium text-ink">{{ d.nome }}</p>
            <p class="truncate text-sm text-muted">{{ d.variavelId }} / {{ d.variavelPreco }}</p>
          </div>
          <div class="flex items-center gap-2">
            <UiBadge v-if="!d.ativo">Inativo</UiBadge>
            <UiButton size="sm" variant="ghost" :disabled="!d.ativo || !data.chaves.escrita || busy === 'enviar'" @click="enviar(d.id)">Enviar</UiButton>
          </div>
        </li>
      </ul>
      <p v-else-if="!loading" class="mt-3 rounded-xl bg-info-soft px-4 py-3 text-sm text-info">Nenhum destino configurado. Crie no painel do crivonft (NFTs › Habbo).</p>
    </section>

    <section class="rounded-2xl border border-line p-5 lg:col-span-2" aria-labelledby="hb-log">
      <h2 id="hb-log" class="text-lg font-semibold text-ink">Últimos envios</h2>
      <ul v-if="data.envios.length" class="mt-3 divide-y divide-line">
        <li v-for="e in data.envios" :key="e.id" class="flex flex-wrap items-center justify-between gap-2 py-3">
          <div class="min-w-0">
            <p class="font-medium text-ink">{{ e.destino }} <span class="font-normal text-muted">· {{ formatDateTime(e.iniciadoEm) }}</span></p>
            <p class="text-sm text-muted">{{ e.gravados }} gravados, {{ e.iguais }} iguais, {{ e.falhas }} falhas{{ e.semPiso ? `, ${e.semPiso} sem piso` : '' }}{{ e.detalhe ? ` — ${e.detalhe}` : '' }}</p>
          </div>
          <UiBadge :tone="estadoEnvio[e.status].tone">{{ estadoEnvio[e.status].label }}</UiBadge>
        </li>
      </ul>
      <p v-else class="mt-2 text-[15px] text-muted">Sem envios registrados.</p>
    </section>
  </div>
</template>
