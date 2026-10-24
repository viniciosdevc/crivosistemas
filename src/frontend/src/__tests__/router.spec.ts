import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { router } from '@/router'
import { useAuthStore } from '@/stores/auth'
import type { Me } from '@/lib/types'

vi.mock('@/lib/api', async (orig) => ({
  ...(await orig<typeof import('@/lib/api')>()),
  api: { get: vi.fn(), post: vi.fn() },
}))

const admin: Me = { id: '1', name: 'Ana', email: 'ana@x.com', role: 'admin' }
const operator: Me = { id: '2', name: 'Beto', email: 'beto@x.com', role: 'operator' }

function signIn(me: Me | null) {
  const auth = useAuthStore()
  auth.me = me
  auth.loaded = true
}

describe('router', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    await router.replace('/esqueci-senha')
  })

  it('o painel tem as telas (e os detalhes) com seção e título', () => {
    const panel = router.getRoutes().filter((r) => r.path.startsWith('/painel') && r.meta.title)
    const titles = panel.map((r) => r.meta.title).sort()
    expect(titles).toEqual(['Calculadora', 'Cliente', 'Clientes', 'Convites', 'Estoque', 'Financeiro', 'Integrações', 'Painel', 'Preços e regras', 'Usuários', 'Venda', 'Venda de Câmbios', 'Vendas'])
    for (const r of panel) {
      expect(r.meta.section, `seção de ${r.meta.title}`).toBeTruthy()
    }
  })

  it('sem login, o painel manda para a entrada com o destino', async () => {
    signIn(null)
    await router.push('/painel/vendas')
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.next).toBe('/painel/vendas')
  })

  it('quem já está logado não vê o login', async () => {
    signIn(operator)
    await router.push('/entrar')
    expect(router.currentRoute.value.path).toBe('/painel')
  })

  it('operador não entra em Usuários, administrador entra', async () => {
    signIn(operator)
    await router.push('/painel/usuarios')
    expect(router.currentRoute.value.path).toBe('/painel')

    signIn(admin)
    await router.push('/painel/usuarios')
    expect(router.currentRoute.value.name).toBe('users')
  })

  it('Preços e regras e Integrações são só do administrador', async () => {
    signIn(operator)
    await router.push('/painel/precos')
    expect(router.currentRoute.value.path).toBe('/painel')
    await router.push('/painel/integracoes')
    expect(router.currentRoute.value.path).toBe('/painel')

    signIn(admin)
    await router.push('/painel/precos')
    expect(router.currentRoute.value.name).toBe('pricing')
  })

  it('o operador abre o estoque', async () => {
    signIn(operator)
    await router.push('/painel/estoque')
    expect(router.currentRoute.value.name).toBe('stock')
  })

  it('o operador abre vendas, clientes e os detalhes', async () => {
    signIn(operator)
    await router.push('/painel/vendas/abc')
    expect(router.currentRoute.value.name).toBe('sale')
    await router.push('/painel/clientes/abc')
    expect(router.currentRoute.value.name).toBe('customer')
  })

  it('não existe cadastro público', () => {
    expect(router.resolve('/cadastro').name).toBe('not-found')
  })
})
