import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import NewSaleForm from '@/components/admin/sales/NewSaleForm.vue'

const get = vi.fn()
const put = vi.fn()
vi.mock('@/lib/api', async (orig) => ({ ...(await orig<typeof import('@/lib/api')>()), api: { get: (...a: unknown[]) => get(...a), post: vi.fn(), put: (...a: unknown[]) => put(...a) } }))

const regras = {
  versao: 1,
  configuracao: { valorKFeira: 13, valorKTrocas: 12, moedasPorK: 1000, dolarPadraoCompra: 5.7, dolarPadraoVenda: 6, taxaPrata: 500, margemVendaCambiosPercent: 20 },
  faixasPix: [{ valorMinUsd: 0, valorMaxUsd: null, multiplicador: 6, ordem: 0, ativo: true }],
}
const estoque = { usd: { saldo: 0, custoMedio: 0 }, esmeraldas: { saldo: 0, custoMedio: 0 } }

async function setup() {
  const w = mount(NewSaleForm, { attachTo: document.body })
  await flushPromises()
  return w
}
const checkbox = (w: ReturnType<typeof mount>, label: string) =>
  w.findAll('[role="checkbox"]').find((c) => c.element.parentElement?.textContent?.includes(label))!

describe('NewSaleForm', () => {
  beforeEach(() => {
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} })
    get.mockImplementation((url: string) => Promise.resolve(url.includes('regras') ? regras : estoque))
  })
  afterEach(() => vi.useRealTimers())

  it('vem como já entregue, com taxa da feira marcada em câmbios e o valor em R$', async () => {
    const w = await setup()
    expect(checkbox(w, 'Já entregue').attributes('aria-checked')).toBe('true')
    expect(checkbox(w, 'Taxa da feira (pratas)').attributes('aria-checked')).toBe('true')
    expect(w.get('[data-testid="taxa-feira"]').text()).toContain('6,50')
    expect(w.text()).not.toContain('Incluir taxa de pratas')
    w.unmount()
  })

  it('a taxa vem marcada em qualquer forma, como na calculadora, e mostra as moedas e o preço', async () => {
    const w = await setup()
    await w.findAll('[role="radio"]').find((r) => r.text() === 'PIX')!.trigger('click')
    expect(checkbox(w, 'Taxa da feira').attributes('aria-checked')).toBe('true')
    expect(w.get('[data-testid="taxa-feira"]').text()).toContain('500 C')
    expect(w.text()).not.toContain('Hora')
    w.unmount()
  })

  it('mostra o avatar do Habbo depois de 400ms digitando o nickname', async () => {
    const w = await setup()
    vi.useFakeTimers()
    await w.get('input[placeholder="Ex: ana"]').setValue('ana')
    expect(w.find('[data-testid="nick-avatar"]').exists()).toBe(false)
    vi.advanceTimersByTime(400)
    await flushPromises()
    expect(w.get('[data-testid="nick-avatar"] img').attributes('src')).toContain('user=ana')
    w.unmount()
  })

  it('modo edição pré-preenche a partir do detalhe e faz PUT sem dataHora', async () => {
    put.mockResolvedValue({ id: 'v9' })
    const venda = {
      venda: { id: 'v9', origem: 'manual', statusNormalizado: 'pendente', formaPagamento: 'hibrido', cliente: { id: 'c', nickname: 'ana', email: null, carteira: null } },
      valores: { custoUsd: 12.5, custoCambios: 0, valorPixBrl: 80.5, valorCambios: 300 },
      itens: [{ descricao: 'Dragão' }],
      manual: { origemCusto: 'estoque_usd', quantidadeEsmeraldas: null, usaTaxaPrata: false, observacao: 'obs' },
    } as never
    const w = mount(NewSaleForm, { attachTo: document.body, props: { venda } })
    await flushPromises()
    expect((w.get('input[placeholder="Nome do item vendido"]').element as HTMLInputElement).value).toBe('Dragão')
    expect((w.get('input[placeholder="Ex: ana"]').element as HTMLInputElement).value).toBe('ana')
    expect((w.get('input[placeholder="0,00"]').element as HTMLInputElement).value).toBe('12,5')
    expect(checkbox(w, 'Já entregue').attributes('aria-checked')).toBe('false')
    expect(w.get('[data-testid="aviso-edicao"]').text()).toContain('recalculados')
    await w.get('form').trigger('submit')
    await flushPromises()
    expect(put).toHaveBeenCalledTimes(1)
    const [url, body] = put.mock.calls[0]! as [string, Record<string, unknown>]
    expect(url).toBe('/api/vendas/v9/manual')
    expect(body).not.toHaveProperty('dataHora')
    expect(body).toMatchObject({ nomeItem: 'Dragão', nicknameComprador: 'ana', custoUsd: 12.5, valorVendaPix: 80.5, valorVendaCambios: 300, usaTaxaPrata: false, entregue: false, observacao: 'obs' })
    expect(w.emitted('saved')).toBeTruthy()
    w.unmount()
  })
})
