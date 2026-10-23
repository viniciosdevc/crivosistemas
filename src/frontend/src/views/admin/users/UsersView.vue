<script setup lang="ts">
import { Pencil, Plus, UserCheck, UserX, Users } from 'lucide-vue-next'
import { ref, watch } from 'vue'
import UserFormModal from '@/components/admin/users/UserFormModal.vue'
import DataTable, { type Column } from '@/components/ui/DataTable.vue'
import PageCard from '@/components/ui/PageCard.vue'
import UiAvatar from '@/components/ui/UiAvatar.vue'
import UiBadge from '@/components/ui/UiBadge.vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiEmptyState from '@/components/ui/UiEmptyState.vue'
import UiMenu, { type MenuItem } from '@/components/ui/UiMenu.vue'
import UiPagination from '@/components/ui/UiPagination.vue'
import UiSearch from '@/components/ui/UiSearch.vue'
import { ApiError, api } from '@/lib/api'
import { confirm } from '@/lib/confirm'
import { formatDateTime } from '@/lib/format'
import { userRole, userStatus } from '@/lib/labels'
import { toast } from '@/lib/toast'
import type { AppUser, Paged } from '@/lib/types'
import { useAsync } from '@/lib/useAsync'

const search = ref('')
const page = ref(1)
const pageSize = 20
const formOpen = ref(false)
const editing = ref<AppUser | null>(null)

const { data, loading, error, run } = useAsync((signal) =>
  api.get<Paged<AppUser>>('/api/users', { search: search.value, page: page.value, pageSize }, signal),
)
watch(search, () => {
  page.value = 1
  void run()
})
watch(page, run)
void run()
watch(error, (e) => e && toast.error('Não foi possível carregar os usuários', e.message))

function openCreate() {
  editing.value = null
  formOpen.value = true
}

function openEdit(u: AppUser) {
  editing.value = u
  formOpen.value = true
}

async function setActive(u: AppUser, active: boolean) {
  if (!active && !(await confirm({
    title: `Desativar ${u.name}?`,
    description: 'A pessoa perde o acesso imediatamente, mas o histórico dela é mantido. Você pode reativar depois.',
    confirmLabel: 'Desativar',
    danger: true,
  }))) return
  try {
    await api.post(`/api/users/${u.id}/${active ? 'activate' : 'deactivate'}`)
    toast.success(active ? 'Usuário reativado' : 'Usuário desativado')
    await run()
  } catch (e) {
    toast.error('Não foi possível concluir', e instanceof ApiError ? e.message : undefined)
  }
}

function menu(u: AppUser): MenuItem[] {
  const items: MenuItem[] = [{ label: 'Editar', icon: Pencil, onSelect: () => openEdit(u) }]
  if (u.isYou) return items
  items.push(u.status === 'active'
    ? { label: 'Desativar', icon: UserX, danger: true, separatorBefore: true, onSelect: () => setActive(u, false) }
    : { label: 'Reativar', icon: UserCheck, separatorBefore: true, onSelect: () => setActive(u, true) })
  return items
}

const columns: Column[] = [
  { key: 'name', label: 'Pessoa' },
  { key: 'role', label: 'Papel' },
  { key: 'status', label: 'Situação', hideBelow: 'sm' },
  { key: 'lastLoginAt', label: 'Último acesso', hideBelow: 'md' },
  { key: 'actions', label: '', align: 'right', class: 'w-16' },
]
</script>

<template>
  <PageCard title="Usuários" subtitle="Quem pode entrar no sistema e com qual papel.">
    <template #actions>
      <UiSearch v-model="search" placeholder="Buscar por nome ou e-mail" />
      <UiButton @click="openCreate"><Plus class="size-4" /> Novo usuário</UiButton>
    </template>

    <DataTable :columns="columns" :rows="data?.items" :loading="loading" :row-key="(u: AppUser) => u.id" :skeleton-rows="3" caption="Usuários">
      <template #cell-name="{ row }">
        <div class="flex items-center gap-3">
          <UiAvatar :name="row.name" size="md" />
          <div class="min-w-0">
            <p class="truncate font-medium text-ink">{{ row.name }} <span v-if="row.isYou" class="font-normal text-muted">(você)</span></p>
            <p class="truncate text-sm text-muted">{{ row.email }}</p>
          </div>
        </div>
      </template>
      <template #cell-role="{ row }"><UiBadge :tone="userRole[row.role]?.tone">{{ userRole[row.role]?.label ?? row.role }}</UiBadge></template>
      <template #cell-status="{ row }"><UiBadge :tone="userStatus[row.status]?.tone">{{ userStatus[row.status]?.label ?? row.status }}</UiBadge></template>
      <template #cell-lastLoginAt="{ row }">{{ row.lastLoginAt ? formatDateTime(row.lastLoginAt) : 'Nunca entrou' }}</template>
      <template #cell-actions="{ row }"><UiMenu :items="menu(row)" :label="`Ações de ${row.name}`" /></template>
      <template #empty>
        <UiEmptyState :icon="Users" title="Nenhum usuário encontrado" :description="search ? 'Tente outro termo de busca.' : 'Crie o primeiro usuário.'" />
      </template>
      <template v-if="data" #footer>
        <UiPagination :page="data.page" :page-size="data.pageSize" :total="data.total" @update:page="page = $event" />
      </template>
    </DataTable>

    <UserFormModal v-model:open="formOpen" :user="editing" @saved="run" />
  </PageCard>
</template>
