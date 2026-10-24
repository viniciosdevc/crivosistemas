<script setup lang="ts">
import { Copy, Link2, Plus, UserPlus, X } from 'lucide-vue-next'
import { ref } from 'vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiTabs from '@/components/ui/UiTabs.vue'
import { ApiError, api } from '@/lib/api'
import { confirm } from '@/lib/confirm'
import { formatDateTime } from '@/lib/format'
import { toast } from '@/lib/toast'
import { useAsync } from '@/lib/useAsync'

interface LinkConvite {
  id: string
  nota: string | null
  criadoEm: string
  primeiroAcessoEm: string | null
  expiraEm: string | null
  acessos: number
  revogadoEm: string | null
}
interface NickConvite {
  id: string
  nickname: string
  nota: string | null
  criadoEm: string
  usadoEm: string | null
  revogadoEm: string | null
}
interface Convites { links: LinkConvite[]; nicks: NickConvite[] }

const tab = ref<'nicks' | 'links'>('nicks')
const nickname = ref('')
const nota = ref('')
const novaUrl = ref<string | null>(null)
const busy = ref(false)

const { data, loading, error, run } = useAsync((signal) => api.get<Convites>('/api/convites', undefined, signal))
void run()

function falha(titulo: string, e: unknown) {
  toast.error(titulo, e instanceof ApiError ? e.message : undefined)
}

async function convidarNick() {
  if (!nickname.value.trim()) return
  busy.value = true
  try {
    await api.post('/api/convites/nicks', { nickname: nickname.value, nota: nota.value || null })
    toast.success('Nick convidado', `${nickname.value.trim()} já pode entrar na loja.`)
    nickname.value = ''
    nota.value = ''
    await run()
  } catch (e) {
    falha('Não foi possível convidar', e)
  } finally {
    busy.value = false
  }
}

async function gerarLink() {
  busy.value = true
  try {
    const r = await api.post<{ url: string }>('/api/convites/links', { nota: nota.value || null })
    novaUrl.value = r.url
    nota.value = ''
    await run()
  } catch (e) {
    falha('Não foi possível gerar o link', e)
  } finally {
    busy.value = false
  }
}

async function copiar() {
  if (!novaUrl.value) return
  try {
    await navigator.clipboard.writeText(novaUrl.value)
    toast.success('Link copiado')
  } catch {
    toast.error('Copie o link manualmente')
  }
}

async function revogar(tipo: 'links' | 'nicks', id: string, nome: string) {
  if (!(await confirm({ title: `Revogar ${nome}?`, description: 'O acesso some na hora. Dá para convidar de novo depois.', confirmLabel: 'Revogar', danger: true }))) return
  try {
    await api.delete(`/api/convites/${tipo}/${id}`)
    toast.success('Convite revogado')
    await run()
  } catch (e) {
    falha('Não foi possível revogar', e)
  }
}

function situacaoLink(l: LinkConvite) {
  if (l.revogadoEm) return { label: 'Revogado', tone: 'neutral' as const }
  if (l.expiraEm && new Date(l.expiraEm) < new Date()) return { label: 'Expirado', tone: 'warning' as const }
  return l.primeiroAcessoEm ? { label: 'Em uso', tone: 'success' as const } : { label: 'Não aberto', tone: 'info' as const }
}

const colunasNicks: Column[] = [
  { key: 'nickname', label: 'Nick' },
  { key: 'situacao', label: 'Situação' },
  { key: 'criadoEm', label: 'Convidado em', hideBelow: 'md' },
  { key: 'actions', label: '', align: 'right', class: 'w-16' },
]
const colunasLinks: Column[] = [
  { key: 'nota', label: 'Nota' },
  { key: 'situacao', label: 'Situação' },
  { key: 'acessos', label: 'Acessos', hideBelow: 'sm' },
  { key: 'criadoEm', label: 'Criado em', hideBelow: 'md' },
  { key: 'actions', label: '', align: 'right', class: 'w-16' },
]
</script>

<template>
  <PageCard title="Convites da loja" subtitle="A loja é só para convidados: libere um nick Habbo ou gere um link de acesso temporário (12 h após o primeiro uso, até 5 aberturas).">
    <div class="mb-6 grid gap-4 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
      <UiInput v-if="tab === 'nicks'" v-model="nickname" label="Nick no Habbo" placeholder="ex: crivo.eth" autocomplete="off" @keydown.enter="convidarNick" />
      <div v-else class="hidden sm:block" />
      <UiInput v-model="nota" label="Nota" optional placeholder="ex: indicação do fulano" />
      <UiButton v-if="tab === 'nicks'" :loading="busy" :disabled="!nickname.trim()" @click="convidarNick"><UserPlus class="size-4" /> Convidar</UiButton>
      <UiButton v-else :loading="busy" @click="gerarLink"><Plus class="size-4" /> Gerar link</UiButton>
    </div>

    <div v-if="novaUrl" class="mb-6 rounded-2xl border border-line bg-surface-2 p-4">
      <p class="text-sm text-muted">Link gerado. Ele só aparece agora — copie e envie.</p>
      <div class="mt-2 flex items-center gap-2">
        <code class="min-w-0 flex-1 truncate rounded-lg bg-surface-3 px-3 py-2 text-sm text-ink">{{ novaUrl }}</code>
        <UiButton variant="secondary" @click="copiar"><Copy class="size-4" /> Copiar</UiButton>
        <UiButton variant="ghost" aria-label="Fechar" @click="novaUrl = null"><X class="size-4" /></UiButton>
      </div>
    </div>

    <UiTabs v-model="tab" :tabs="[{ value: 'nicks', label: 'Nicks', count: data?.nicks.filter((n) => !n.revogadoEm).length }, { value: 'links', label: 'Links', count: data?.links.filter((l) => !l.revogadoEm).length }]" class="mb-4" />
    <p v-if="error" class="mb-4 text-sm text-danger">{{ error.message }}</p>

    <DataTable v-if="tab === 'nicks'" :columns="colunasNicks" :rows="data?.nicks" :loading="loading" :row-key="(n: NickConvite) => n.id" :skeleton-rows="3" caption="Nicks convidados">
      <template #cell-nickname="{ row }">
        <p class="font-medium text-ink">{{ row.nickname }}</p>
        <p v-if="row.nota" class="truncate text-sm text-muted">{{ row.nota }}</p>
      </template>
      <template #cell-situacao="{ row }">
        <UiBadge :tone="row.revogadoEm ? 'neutral' : row.usadoEm ? 'success' : 'info'">{{ row.revogadoEm ? 'Revogado' : row.usadoEm ? 'Já entrou' : 'Aguardando' }}</UiBadge>
      </template>
      <template #cell-criadoEm="{ row }">{{ formatDateTime(row.criadoEm) }}</template>
      <template #cell-actions="{ row }">
        <UiButton v-if="!row.revogadoEm" variant="ghost" size="sm" @click="revogar('nicks', row.id, row.nickname)">Revogar</UiButton>
      </template>
      <template #empty><UiEmptyState :icon="UserPlus" title="Nenhum nick convidado" description="Convide o primeiro nick acima." /></template>
    </DataTable>

    <DataTable v-else :columns="colunasLinks" :rows="data?.links" :loading="loading" :row-key="(l: LinkConvite) => l.id" :skeleton-rows="3" caption="Links de convidado">
      <template #cell-nota="{ row }">{{ row.nota || 'Sem nota' }}</template>
      <template #cell-situacao="{ row }"><UiBadge :tone="situacaoLink(row).tone">{{ situacaoLink(row).label }}</UiBadge></template>
      <template #cell-criadoEm="{ row }">{{ formatDateTime(row.criadoEm) }}</template>
      <template #cell-actions="{ row }">
        <UiButton v-if="!row.revogadoEm" variant="ghost" size="sm" @click="revogar('links', row.id, 'o link')">Revogar</UiButton>
      </template>
      <template #empty><UiEmptyState :icon="Link2" title="Nenhum link" description="Gere o primeiro link acima." /></template>
    </DataTable>
  </PageCard>
</template>
