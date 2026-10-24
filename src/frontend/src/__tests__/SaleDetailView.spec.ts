import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import SaleDetailView from '@/views/admin/SaleDetailView.vue'
import { limparCacheImagens } from '@/lib/imagens'

const get = vi.fn()
vi.mock('@/lib/api', async (orig) => ({ ...(await orig<typeof import('@/lib/api')>()), api: { get: (...a: unknown[]) => get(...a), put: vi.fn(), post: vi.fn() } }))

const regras = {
  versao: 1,
  configuracao: { valorKFeira: 13, valorKTrocas: 12, moedasPorK: 1000, dolarPadraoCompra: 5.7, dolarPadraoVenda: 6, taxaPrata: 500, margemVendaCambiosPercent: 20 },
  faixasPix: [{ valorMinUsd: 0, valorMaxUsd: null, multiplicador: 6, ordem: 0, ativo: true }],
}
const estoque = { usd: { saldo: 0, custoMedio: 0 }, esmeraldas: { saldo: 0, custoMedio: 0 } }

function detalhe(origem: string, status: string) {
  return {
    venda: { id: 'v1', origem, origemId: 'o1', pedidoId: null, dataHora: '2026-10-22T15:00:00Z', status, statusNormalizado: status, formaPagamento: 'pix', vendedor: null, cliente: null, item: 'Dragão', removida: false, faturavel: true },
    valores: { totalBrl: 1, descontoBrl: 0, taxaBrl: 0, valorPixBrl: 1, valorCambios: 0, custoBrl: 0, custoUsd: 0, custoCambios: 0, lucroBrl: 1, margemPercent: 1 },
    cupom: null, itens: [{ ordem: 0, descricao: 'Dragão', quantidade: 1, precoUsd: 0, precoBrl: 1, referencia: null }], atualizadoEmOrigem: '2026-10-22T15:00:00Z', removidaEm: null, eventos: [],
    manual: origem === 'manual' ? { origemCusto: 'estoque_usd', quantidadeEsmeraldas: null, usaTaxaPrata: true, dolarCompraUsado: null, observacao: null, movimentos: [] } : null,
  }
}

async function setup(d: unknown, url = '/painel/vendas/v1') {
  get.mockImplementation((u: string) => Promise.resolve(u.includes('/api/vendas/') ? d : u.includes('regras') ? regras : estoque))
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/painel/vendas', name: 'sales', component: { render: () => null } },
      { path: '/painel/vendas/:id', name: 'sale', component: SaleDetailView },
      { path: '/painel/clientes/:id', name: 'customer', component: { render: () => null } },
    ],
  })
  await router.push(url)
  const wrapper = mount(SaleDetailView, { global: { plugins: [router] }, attachTo: document.body })
  await flushPromises()
  return { wrapper, router }
}
const editBtn = (w: ReturnType<typeof mount>) => w.findAll('button').find((b) => b.text().includes('Editar'))

describe('SaleDetailView edição', () => {
  beforeEach(() => {
    limparCacheImagens()
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} })
  })

  it('manual pendente tem Editar habilitado', async () => {
    const { wrapper } = await setup(detalhe('manual', 'pendente'))
    expect(editBtn(wrapper)!.attributes('disabled')).toBeUndefined()
    expect(wrapper.find('[data-testid="edit-hint"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('manual cancelada desabilita Editar com aviso', async () => {
    const { wrapper } = await setup(detalhe('manual', 'cancelado'))
    expect(editBtn(wrapper)!.attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="edit-hint"]').text()).toBe('Venda cancelada não pode ser editada.')
    wrapper.unmount()
  })

  it('venda do crivonft desabilita Editar com aviso', async () => {
    const { wrapper } = await setup(detalhe('nft', 'pago'))
    expect(editBtn(wrapper)!.attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="edit-hint"]').text()).toContain('Altere no crivonft.')
    wrapper.unmount()
  })

  it('?editar=1 abre o modal e remove a query', async () => {
    const { wrapper, router } = await setup(detalhe('manual', 'pendente'), '/painel/vendas/v1?editar=1')
    expect(document.body.textContent).toContain('Salvar alterações')
    expect(router.currentRoute.value.query.editar).toBeUndefined()
    wrapper.unmount()
  })
})
