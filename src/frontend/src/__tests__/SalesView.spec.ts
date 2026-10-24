import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import SalesView from '@/views/admin/SalesView.vue'
import { limparCacheImagens } from '@/lib/imagens'
import type { VendaLista } from '@/lib/types'

const get = vi.fn()
vi.mock('@/lib/api', async (orig) => ({
  ...(await orig<typeof import('@/lib/api')>()),
  api: { get: (...a: unknown[]) => get(...a) },
}))

const venda: VendaLista = {
  id: 'v1', origem: 'nft', origemId: 'o-1', pedidoId: '1001', dataHora: '2026-10-22T15:00:00Z', dia: '2026-10-22', status: 'paid', statusNormalizado: 'pago',
  formaPagamento: 'pix', vendedor: null, item: 'Dragão Azul +2', cliente: { id: 'c1', nickname: 'Ana', email: 'ana@x.com', carteira: null }, totalBrl: 150, lucroBrl: 30, margemPercent: 20, removida: false, faturavel: true,
}

async function setup(url = '/painel/vendas') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/painel/vendas', name: 'sales', component: SalesView },
      { path: '/painel/vendas/:id', name: 'sale', component: { render: () => null } },
      { path: '/painel/clientes/:id', name: 'customer', component: { render: () => null } },
    ],
  })
  await router.push(url)
  const wrapper = mount(SalesView, { global: { plugins: [router] } })
  await flushPromises()
  return { router, wrapper }
}

describe('SalesView', () => {
  beforeEach(() => {
    limparCacheImagens()
    get.mockReset()
    get.mockResolvedValue({ items: [venda], page: 1, pageSize: 20, total: 1 })
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} })
  })

  it('carrega a lista e mostra venda, cliente, status e total', async () => {
    const { wrapper } = await setup()
    expect(get).toHaveBeenCalledWith('/api/vendas', expect.objectContaining({ page: 1, pageSize: 20, ordem: 'data', dir: 'desc', origem: '', busca: '' }), expect.anything())
    const text = wrapper.text()
    expect(text).toContain('Dragão Azul +2')
    expect(text).toContain('Ana')
    expect(text).toContain('Pago')
    expect(text).toContain('R$ 150,00')
  })

  it('aplica na API os filtros que vêm da URL', async () => {
    await setup('/painel/vendas?origem=cambios&status=pendente&pagamento=pix&busca=ana&de=2026-10-01&ate=2026-10-15&page=2')
    expect(get).toHaveBeenCalledWith(
      '/api/vendas',
      expect.objectContaining({ origem: 'cambios', statusNormalizado: 'pendente', formaPagamento: 'pix', busca: 'ana', de: '2026-10-01', ate: '2026-10-15', page: 2 }),
      expect.anything(),
    )
  })

  it('paginar e limpar filtros atualiza a URL', async () => {
    get.mockResolvedValue({ items: [venda], page: 1, pageSize: 20, total: 55 })
    const { router, wrapper } = await setup('/painel/vendas?origem=nft')
    await wrapper.find('[aria-label="Próxima página"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({ origem: 'nft', page: '2' })
    expect(get).toHaveBeenLastCalledWith('/api/vendas', expect.objectContaining({ origem: 'nft', page: 2 }), expect.anything())

    await wrapper.findAll('button').find((b) => b.text().includes('Limpar filtros'))!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({})
    expect(get).toHaveBeenLastCalledWith('/api/vendas', expect.objectContaining({ origem: '', page: 1 }), expect.anything())
  })

  it('ordenar por lucro grava a ordem na URL', async () => {
    const { router, wrapper } = await setup()
    await wrapper.findAll('th button').find((b) => b.text().includes('Lucro'))!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({ ordem: 'lucro' })
  })

  it('mostra o estado vazio com filtros e o erro com botão de nova tentativa', async () => {
    get.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0 })
    const { wrapper } = await setup('/painel/vendas?origem=nft')
    expect(wrapper.text()).toContain('Nenhuma venda encontrada')
    expect(wrapper.text()).toContain('Nenhuma venda com esses filtros')

    const { ApiError } = await import('@/lib/api')
    get.mockRejectedValueOnce(new ApiError(500, 'Servidor indisponível'))
    const second = await setup()
    expect(second.wrapper.text()).toContain('Não foi possível carregar as vendas')
    expect(second.wrapper.text()).toContain('Tentar de novo')
  })

  it('mostra a miniatura do item, o placeholder sem imagem e o fallback sem nome', async () => {
    get.mockImplementation((url: string) =>
      url.startsWith('/api/vendas/imagens')
        ? Promise.resolve({ imagens: { 'Dragão Azul': 'https://img/x.png' } })
        : Promise.resolve({ items: [venda, { ...venda, id: 'v2', item: null, origemId: 'abcdef123456' }], page: 1, pageSize: 20, total: 2 }),
    )
    const { wrapper } = await setup()
    expect(wrapper.find('img[src="https://img/x.png"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="image-placeholder"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Venda abcdef12')
  })
})
