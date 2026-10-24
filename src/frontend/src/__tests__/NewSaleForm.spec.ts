import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import NewSaleForm from '@/components/admin/sales/NewSaleForm.vue'

const get = vi.fn()
vi.mock('@/lib/api', async (orig) => ({ ...(await orig<typeof import('@/lib/api')>()), api: { get: (...a: unknown[]) => get(...a), post: vi.fn() } }))

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

  it('a taxa acompanha a forma até o usuário mexer nela', async () => {
    const w = await setup()
    const pix = w.findAll('[role="radio"]').find((r) => r.text() === 'PIX')!
    const cambios = w.findAll('[role="radio"]').find((r) => r.text() === 'Câmbios')!
    await pix.trigger('click')
    expect(checkbox(w, 'Taxa da feira').attributes('aria-checked')).toBe('false')
    await cambios.trigger('click')
    expect(checkbox(w, 'Taxa da feira').attributes('aria-checked')).toBe('true')
    await checkbox(w, 'Taxa da feira').trigger('click')
    await pix.trigger('click')
    await cambios.trigger('click')
    expect(checkbox(w, 'Taxa da feira').attributes('aria-checked')).toBe('false')
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
})
