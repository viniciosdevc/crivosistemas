<script setup lang="ts">
import {
  Boxes, Calculator, Search, HandCoins, ChartColumn, ChevronDown, Coins, KeyRound, LogOut, Menu, Moon, PanelLeftClose, PanelLeftOpen, Plug, Plus, ReceiptText, SlidersHorizontal, Sun, UserCog, Users, X,
  Building2, ClipboardCheck, Globe2, Layers, Mail, Wallet, ChartPie,
} from 'lucide-vue-next'
import { DropdownMenuContent, DropdownMenuItem, DropdownMenuPortal, DropdownMenuRoot, DropdownMenuTrigger } from 'reka-ui'
import { computed, ref, watch, type Component } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import BrandMark from '@/components/BrandMark.vue'
import ChangePasswordModal from '@/components/admin/ChangePasswordModal.vue'
import QuickActionsModal, { type QuickTab } from '@/components/admin/QuickActionsModal.vue'
import UiAvatar from '@/components/ui/UiAvatar.vue'
import { isDark, restoreTheme, toggleTheme } from '@/lib/theme'
import type { VendaLista } from '@/lib/types'
import { useAuthStore } from '@/stores/auth'

interface NavItem { label: string; to: string; icon: Component; exact?: boolean; adminOnly?: boolean }

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const collapsed = ref(false)
const mobileOpen = ref(false)
const passwordOpen = ref(false)
const quickOpen = ref(false)
const quickTab = ref<QuickTab>('nft')
function openQuick(t: QuickTab) {
  quickTab.value = t
  quickOpen.value = true
}
restoreTheme()

interface NavGroup { key: string; label: string; icon: Component; items: NavItem[] }

const top: NavItem = { label: 'Painel', to: '/painel', icon: ChartColumn, exact: true }
const groups: NavGroup[] = [
  {
    key: 'operacoes', label: 'Operações', icon: ReceiptText, items: [
      { label: 'Vendas', to: '/painel/vendas', icon: ReceiptText },
      { label: 'Clientes', to: '/painel/clientes', icon: Users },
      { label: 'Venda de Câmbios', to: '/painel/cambios', icon: HandCoins },
    ],
  },
  {
    key: 'ativos', label: 'Ativos', icon: Boxes, items: [
      { label: 'Estoque', to: '/painel/estoque', icon: Boxes },
      { label: 'NFTs', to: '/painel/nfts', icon: Layers, adminOnly: true },
    ],
  },
  {
    key: 'gestao', label: 'Gestão', icon: Wallet, items: [
      { label: 'Financeiro', to: '/painel/financeiro', icon: Wallet, adminOnly: true },
      { label: 'Posição e saldos', to: '/painel/posicao', icon: ClipboardCheck, adminOnly: true },
      { label: 'Convites da loja', to: '/painel/convites', icon: Mail, adminOnly: true },
      { label: 'Preços e regras', to: '/painel/precos', icon: SlidersHorizontal, adminOnly: true },
    ],
  },
  {
    key: 'relatorios', label: 'Relatórios', icon: ChartPie, items: [
      { label: 'Vendas', to: '/painel/relatorios/vendas', icon: ReceiptText },
      { label: 'Mapa de acessos', to: '/painel/relatorios/acessos', icon: Globe2, adminOnly: true },
      { label: 'Financeiro', to: '/painel/relatorios/financeiro', icon: Wallet, adminOnly: true },
    ],
  },
  {
    key: 'sistema', label: 'Sistema', icon: UserCog, items: [
      { label: 'Integrações', to: '/painel/integracoes', icon: Plug, adminOnly: true },
      { label: 'Usuários', to: '/painel/usuarios', icon: UserCog, adminOnly: true },
    ],
  },
]
const visibleGroups = computed(() =>
  groups
    .map((g) => ({ ...g, items: g.items.filter((i) => !i.adminOnly || auth.isAdmin) }))
    .filter((g) => g.items.length > 0),
)
// Grupos começam fechados; abre o da rota atual e lembra o que foi aberto à mão.
const STORAGE_KEY = 'crivosistemas:nav-open'
function loadOpen(): Set<string> {
  try {
    const raw = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]')
    return new Set(Array.isArray(raw) ? raw.filter((k) => typeof k === 'string') : [])
  } catch {
    return new Set()
  }
}
const opened = ref<Set<string>>(loadOpen())
function isOpen(g: NavGroup) {
  return opened.value.has(g.key) || g.items.some(isActive)
}
function toggle(g: NavGroup) {
  const next = new Set(opened.value)
  if (isOpen(g)) next.delete(g.key)
  else next.add(g.key)
  opened.value = next
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify([...next]))
  } catch { /* sem storage: só não lembra */ }
}

function isActive(item: NavItem) {
  if (item.exact) return route.path === item.to
  return route.path === item.to || route.path.startsWith(item.to + '/')
}

watch(() => route.fullPath, () => (mobileOpen.value = false))

const crumbs = computed(() => [route.meta.section, route.meta.title].filter(Boolean) as string[])
const roleLabel = computed(() => (auth.me?.role === 'admin' ? 'Administrador' : 'Operador'))

function onSaleCreated(venda: VendaLista) {
  void router.push(`/painel/vendas/${venda.id}`)
}

async function logout() {
  await auth.logout()
  router.replace({ name: 'login' })
}
</script>

<template>
  <div class="flex min-h-dvh flex-col lg:min-h-0 lg:fixed lg:inset-0">
  <div class="min-h-0 flex-1 bg-canvas lg:flex">
    <!-- Barra superior no celular -->
    <div class="sticky top-0 z-30 flex h-16 items-center justify-between bg-canvas/95 px-4 backdrop-blur lg:hidden">
      <button type="button" class="-ml-2 rounded-full p-2 text-ink hover:bg-surface-3" aria-label="Abrir menu" @click="mobileOpen = true">
        <Menu class="size-6" />
      </button>
      <BrandMark />
      <div class="-mr-2 flex items-center">
        <button type="button" class="rounded-full p-2 text-ink hover:bg-surface-3" aria-label="Calculadora" @click="openQuick('nft')">
          <Calculator class="size-5" />
        </button>
        <button type="button" class="rounded-full p-2 text-ink hover:bg-surface-3" aria-label="Buscar NFT" @click="openQuick('busca')">
          <Search class="size-5" />
        </button>
        <button type="button" class="rounded-full p-2 text-ink hover:bg-surface-3" aria-label="Moedas" @click="openQuick('moedas')">
          <Coins class="size-5" />
        </button>
        <button type="button" class="rounded-full p-2 text-ink hover:bg-surface-3" aria-label="Nova venda" @click="openQuick('venda')">
          <Plus class="size-5" />
        </button>
        <button type="button" class="rounded-full p-2 text-ink hover:bg-surface-3" :aria-label="isDark ? 'Tema claro' : 'Tema escuro'" @click="toggleTheme">
          <Sun v-if="isDark" class="size-5" /><Moon v-else class="size-5" />
        </button>
      </div>
    </div>

    <div v-if="mobileOpen" class="fixed inset-0 z-40 bg-overlay animate-fade-in lg:hidden" @click="mobileOpen = false" />

    <aside
      :class="[
        'fixed inset-y-0 left-0 z-50 flex flex-col bg-canvas transition-[transform,width] duration-200 lg:sticky lg:top-0 lg:h-full lg:translate-x-0',
        mobileOpen ? 'translate-x-0 shadow-2xl' : '-translate-x-full',
        collapsed ? 'w-[84px]' : 'w-[272px]',
      ]"
      aria-label="Menu principal"
    >
      <div :class="['flex h-[76px] shrink-0 items-center', collapsed ? 'justify-center px-3' : 'justify-between px-5']">
        <RouterLink to="/painel" class="rounded-lg"><BrandMark :compact="collapsed" /></RouterLink>
        <button
          v-if="!collapsed"
          type="button"
          class="hidden rounded-lg p-1.5 text-muted hover:bg-surface-3 hover:text-ink lg:block"
          aria-label="Recolher menu"
          @click="collapsed = true"
        >
          <PanelLeftClose class="size-5" stroke-width="1.6" />
        </button>
        <button type="button" class="rounded-lg p-1.5 text-muted hover:text-ink lg:hidden" aria-label="Fechar menu" @click="mobileOpen = false">
          <X class="size-5" />
        </button>
      </div>
      <button
        v-if="collapsed"
        type="button"
        class="mx-auto mb-2 hidden rounded-lg p-1.5 text-muted hover:bg-surface-3 hover:text-ink lg:block"
        aria-label="Expandir menu"
        @click="collapsed = false"
      >
        <PanelLeftOpen class="size-5" stroke-width="1.6" />
      </button>

      <nav class="scrollbar-thin mt-3 flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto px-3 pb-4">
        <RouterLink
          :to="top.to"
          :title="collapsed ? top.label : undefined"
          :aria-current="isActive(top) ? 'page' : undefined"
          :class="[
            'flex h-12 cursor-pointer items-center gap-3 rounded-xl text-base transition-colors',
            collapsed ? 'justify-center' : 'px-3',
            isActive(top) ? 'bg-brand font-medium text-brand-ink' : 'text-text hover:bg-surface-3 hover:text-ink',
          ]"
        >
          <component :is="top.icon" class="size-5 shrink-0" stroke-width="1.7" />
          <span v-if="!collapsed" class="flex-1 truncate">{{ top.label }}</span>
        </RouterLink>
        <template v-for="g in visibleGroups" :key="g.key">
          <button
            v-if="!collapsed"
            type="button"
            class="mt-2 flex h-10 w-full cursor-pointer items-center justify-between rounded-xl px-3 text-[13px] font-semibold uppercase tracking-wide text-text transition-colors hover:bg-surface-3 hover:text-ink"
            :aria-expanded="isOpen(g)"
            @click="toggle(g)"
          >
            {{ g.label }}
            <ChevronDown :class="['size-5 shrink-0 transition-transform', !isOpen(g) && '-rotate-90']" />
          </button>
          <div v-else class="mx-2 my-1 border-t border-line" />
          <template v-if="collapsed || isOpen(g)">
            <template v-for="item in g.items" :key="item.to">
          <RouterLink
            :to="item.to"
            :title="collapsed ? item.label : undefined"
            :aria-current="isActive(item) ? 'page' : undefined"
            :class="[
              'flex h-12 cursor-pointer items-center gap-3 rounded-xl text-base transition-colors',
              collapsed ? 'justify-center' : 'pl-5 pr-3',
              isActive(item) ? 'bg-brand font-medium text-brand-ink' : 'text-text hover:bg-surface-3 hover:text-ink',
            ]"
          >
            <component :is="item.icon" class="size-5 shrink-0" stroke-width="1.7" />
            <span v-if="!collapsed" class="flex-1 truncate">{{ item.label }}</span>
          </RouterLink>
            </template>
          </template>
        </template>
      </nav>

      <div class="mx-3 border-t border-line py-4">
        <DropdownMenuRoot :modal="false">
          <DropdownMenuTrigger :class="['flex w-full items-center gap-3 rounded-xl p-2 text-left hover:bg-surface-3', collapsed && 'justify-center']">
            <UiAvatar :name="auth.me?.name ?? ''" size="sm" />
            <template v-if="!collapsed">
              <span class="min-w-0 flex-1">
                <span class="block truncate text-[15px] text-ink">{{ auth.me?.name.split(' ')[0] }}</span>
                <span class="block truncate text-xs text-muted">{{ roleLabel }}</span>
              </span>
              <ChevronDown class="size-4 text-muted" />
            </template>
          </DropdownMenuTrigger>
          <DropdownMenuPortal>
            <DropdownMenuContent side="top" align="start" :side-offset="6" class="z-50 w-60 rounded-2xl border border-line bg-surface p-1.5 shadow-[0_16px_40px_-12px_rgb(0_0_0/0.18)]">
              <div class="px-3 py-2">
                <p class="truncate text-sm font-medium text-ink">{{ auth.me?.name }}</p>
                <p class="truncate text-xs text-muted">{{ auth.me?.email }}</p>
              </div>
              <DropdownMenuItem class="flex cursor-pointer items-center gap-2.5 rounded-xl px-3 py-2.5 text-sm text-text outline-none data-[highlighted]:bg-surface-3" @select="passwordOpen = true">
                <KeyRound class="size-4" /> Alterar senha
              </DropdownMenuItem>
              <DropdownMenuItem class="flex cursor-pointer items-center gap-2.5 rounded-xl px-3 py-2.5 text-sm text-text outline-none data-[highlighted]:bg-surface-3" @select="toggleTheme">
                <Sun v-if="isDark" class="size-4" /><Moon v-else class="size-4" /> {{ isDark ? 'Tema claro' : 'Tema escuro' }}
              </DropdownMenuItem>
              <DropdownMenuItem class="flex cursor-pointer items-center gap-2.5 rounded-xl px-3 py-2.5 text-sm text-danger outline-none data-[highlighted]:bg-danger-soft" @select="logout">
                <LogOut class="size-4" /> Sair
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenuPortal>
        </DropdownMenuRoot>
      </div>
    </aside>

    <div class="flex min-w-0 flex-1 flex-col lg:h-full lg:pr-6">
      <header class="hidden h-[76px] shrink-0 items-center justify-between gap-4 pr-1 pl-3 lg:flex">
        <nav aria-label="Trilha" class="flex items-center gap-2 text-[15px]">
          <Building2 class="size-4 text-muted" stroke-width="1.7" />
          <template v-for="(c, i) in crumbs" :key="c">
            <span v-if="i > 0" class="text-faint">/</span>
            <span :class="i === crumbs.length - 1 ? 'text-ink' : 'text-muted'">{{ c }}</span>
          </template>
        </nav>
        <div class="flex items-center gap-1">
          <button type="button" class="rounded-full p-2.5 text-ink hover:bg-surface-3" aria-label="Calculadora" title="Calculadora" @click="openQuick('nft')">
            <Calculator class="size-5" stroke-width="1.7" />
          </button>
          <button type="button" class="rounded-full p-2.5 text-ink hover:bg-surface-3" aria-label="Buscar NFT" title="Buscar NFT" @click="openQuick('busca')">
            <Search class="size-5" stroke-width="1.7" />
          </button>
          <button type="button" class="rounded-full p-2.5 text-ink hover:bg-surface-3" aria-label="Moedas" title="Moedas" @click="openQuick('moedas')">
            <Coins class="size-5" stroke-width="1.7" />
          </button>
          <button type="button" class="rounded-full p-2.5 text-ink hover:bg-surface-3" aria-label="Nova venda" title="Nova venda" @click="openQuick('venda')">
            <Plus class="size-5" stroke-width="1.7" />
          </button>
          <button type="button" class="rounded-full p-2.5 text-ink hover:bg-surface-3" :aria-label="isDark ? 'Tema claro' : 'Tema escuro'" @click="toggleTheme">
            <Sun v-if="isDark" class="size-5" stroke-width="1.7" /><Moon v-else class="size-5" stroke-width="1.7" />
          </button>
        </div>
      </header>
      <main class="scrollbar-thin min-h-0 flex-1 lg:overflow-y-auto lg:rounded-t-[var(--radius-card)]">
        <RouterView v-slot="{ Component: View, route: r }">
          <Transition name="page" mode="out-in">
            <component :is="View" :key="r.path" />
          </Transition>
        </RouterView>
      </main>
    </div>
  </div>
  <ChangePasswordModal v-model:open="passwordOpen" />
  <QuickActionsModal v-model:open="quickOpen" v-model:tab="quickTab" @created="onSaleCreated" />
  </div>
</template>
