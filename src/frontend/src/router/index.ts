import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

declare module 'vue-router' {
  interface RouteMeta {
    /** Páginas do painel: seção (trilha) e nome da página. */
    section?: string
    title?: string
    guest?: boolean
    requiresAuth?: boolean
    /** Só administradores entram. */
    adminOnly?: boolean
  }
}

const admin: RouteRecordRaw[] = [
  { path: '', name: 'dashboard', component: () => import('@/views/admin/DashboardView.vue'), meta: { section: 'Gestão', title: 'Painel' } },
  { path: 'vendas', name: 'sales', component: () => import('@/views/admin/SalesView.vue'), meta: { section: 'Comercial', title: 'Vendas' } },
  { path: 'vendas/:id', name: 'sale', component: () => import('@/views/admin/SaleDetailView.vue'), meta: { section: 'Comercial', title: 'Venda' } },
  { path: 'clientes', name: 'customers', component: () => import('@/views/admin/CustomersView.vue'), meta: { section: 'Comercial', title: 'Clientes' } },
  { path: 'clientes/:id', name: 'customer', component: () => import('@/views/admin/CustomerDetailView.vue'), meta: { section: 'Comercial', title: 'Cliente' } },
  { path: 'calculadora', name: 'calculator', component: () => import('@/views/admin/CalculatorView.vue'), meta: { section: 'Comercial', title: 'Calculadora' } },
  { path: 'estoque', name: 'stock', component: () => import('@/views/admin/StockView.vue'), meta: { section: 'Comercial', title: 'Estoque' } },
  { path: 'cambios', name: 'cambios', component: () => import('@/views/admin/CambiosView.vue'), meta: { section: 'Comercial', title: 'Venda de Câmbios' } },
  { path: 'nfts', name: 'nfts', component: () => import('@/views/admin/NftsView.vue'), meta: { section: 'Ativos', title: 'NFTs', adminOnly: true } },
  { path: 'financeiro', name: 'finance', component: () => import('@/views/admin/FinanceView.vue'), meta: { section: 'Gestão', title: 'Financeiro', adminOnly: true } },
  { path: 'posicao', name: 'position', component: () => import('@/views/admin/PositionView.vue'), meta: { section: 'Gestão', title: 'Posição e saldos', adminOnly: true } },
  { path: 'convites', name: 'invites', component: () => import('@/views/admin/ConvitesView.vue'), meta: { section: 'Gestão', title: 'Convites', adminOnly: true } },
  { path: 'precos', name: 'pricing', component: () => import('@/views/admin/PricingView.vue'), meta: { section: 'Gestão', title: 'Preços e regras', adminOnly: true } },
  { path: 'integracoes', name: 'integrations', component: () => import('@/views/admin/IntegrationsView.vue'), meta: { section: 'Configurações', title: 'Integrações', adminOnly: true } },
  { path: 'usuarios', name: 'users', component: () => import('@/views/admin/users/UsersView.vue'), meta: { section: 'Administração', title: 'Usuários', adminOnly: true } },
]

const routes: RouteRecordRaw[] = [
  { path: '/', name: 'home', component: () => import('@/views/HomeView.vue') },
  { path: '/entrar', name: 'login', component: () => import('@/views/auth/LoginView.vue'), meta: { guest: true } },
  { path: '/esqueci-senha', name: 'forgot-password', component: () => import('@/views/auth/ForgotPasswordView.vue') },
  { path: '/redefinir-senha', name: 'reset-password', component: () => import('@/views/auth/ResetPasswordView.vue') },
  { path: '/painel', component: () => import('@/layouts/AdminLayout.vue'), meta: { requiresAuth: true }, children: admin },
  { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('@/views/NotFoundView.vue') },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: (to, from, saved) => saved ?? (to.path !== from.path ? { top: 0 } : undefined),
})

router.beforeEach(async (to) => {
  if (!to.matched.some((r) => r.meta.requiresAuth || r.meta.guest || r.meta.adminOnly) && to.name !== 'home') return true
  const auth = useAuthStore()
  if (!auth.loaded) await auth.load()
  if (to.matched.some((r) => r.meta.requiresAuth) && !auth.me) return { name: 'login', query: { next: to.fullPath } }
  if (to.matched.some((r) => r.meta.adminOnly) && !auth.isAdmin) return { path: '/painel' }
  if (to.meta.guest && auth.me) return { path: '/painel' }
  return true
})
