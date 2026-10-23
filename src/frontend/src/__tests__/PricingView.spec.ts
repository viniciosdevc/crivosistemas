import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PricingView from '@/views/admin/PricingView.vue'
import { ApiError } from '@/lib/api'
import type { Precificacao } from '@/lib/types'

const get = vi.fn()
const put = vi.fn()
const post = vi.fn()
vi.mock('@/lib/api', async (orig) => ({
  ...(await orig<typeof import('@/lib/api')>()),
  api: { get: (...a: unknown[]) => get(...a), put: (...a: unknown[]) => put(...a), post: (...a: unknown[]) => post(...a) },
}))
vi.mock('@/lib/confirm', () => ({ confirm: vi.fn().mockResolvedValue(true) }))

const rules = (versao: number, dolar = 6): Precificacao => ({
  versao,
  configuracao: { valorKFeira: 13, valorKTrocas: 12, moedasPorK: 1000, dolarPadraoCompra: 5.7, dolarPadraoVenda: dolar, taxaPrata: 3, margemVendaCambiosPercent: 20 },
  faixasPix: [
    { valorMinUsd: 0, valorMaxUsd: 10, multiplicador: 6.2, ordem: 0, ativo: true },
    { valorMinUsd: 10.01, valorMaxUsd: null, multiplicador: 6, ordem: 1, ativo: true },
  ],
  ultimoEnvio: { id: 'e1', versao, estado: 'falhou', tentativas: 3, criadoEm: '2026-10-22T10:00:00Z', ultimaTentativaEm: '2026-10-22T10:05:00Z', proximaTentativaEm: null, enviadoEm: null, ultimoStatusHttp: 502, ultimoErro: 'Bad gateway' },
  envioConfigurado: true,
})

const button = (w: ReturnType<typeof mount>, text: string) => w.findAll('button').find((b) => b.text().includes(text))!

async function setup() {
  const wrapper = mount(PricingView, { global: { stubs: { RouterLink: true } } })
  await flushPromises()
  return wrapper
}

describe('PricingView', () => {
  beforeEach(() => {
    get.mockReset()
    put.mockReset()
    post.mockReset()
    get.mockResolvedValue(rules(4))
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} })
  })

  it('mostra a configuração, as faixas e o status do último envio', async () => {
    const w = await setup()
    expect(w.text()).toContain('Versão 4')
    expect(w.findAll('[data-testid="tier-row"]')).toHaveLength(2)
    expect((w.find('input[inputmode="decimal"]').element as HTMLInputElement).value).toBe('13')
    expect(w.text()).toContain('Falhou')
    expect(w.text()).toContain('Bad gateway')
    expect(button(w, 'Reenviar agora')).toBeTruthy()
  })

  it('só mostra a barra de salvar depois de editar', async () => {
    const w = await setup()
    expect(w.text()).not.toContain('Salvar regras')
    await w.find('input[inputmode="decimal"]').setValue('14')
    expect(w.text()).toContain('Salvar regras')
    await button(w, 'Descartar').trigger('click')
    expect(w.text()).not.toContain('Salvar regras')
  })

  it('salva enviando a versão carregada e as faixas em ordem', async () => {
    put.mockResolvedValue(rules(5))
    const w = await setup()
    await w.find('input[inputmode="decimal"]').setValue('14,5')
    await button(w, 'Salvar regras').trigger('click')
    await flushPromises()
    expect(put).toHaveBeenCalledTimes(1)
    const [path, body] = put.mock.calls[0] as [string, { versao: number; configuracao: { valorKFeira: number }; faixasPix: { ordem: number }[] }]
    expect(path).toBe('/api/precificacao')
    expect(body.versao).toBe(4)
    expect(body.configuracao.valorKFeira).toBe(14.5)
    expect(body.faixasPix.map((f) => f.ordem)).toEqual([0, 1])
    expect(w.text()).toContain('Versão 5')
    expect(w.text()).not.toContain('Salvar regras')
  })

  it('não envia com campo inválido', async () => {
    const w = await setup()
    await w.find('input[inputmode="decimal"]').setValue('abc')
    await button(w, 'Salvar regras').trigger('click')
    await flushPromises()
    expect(put).not.toHaveBeenCalled()
    expect(w.text()).toContain('Corrija os campos')
  })

  it('409: avisa que mudou e recarrega a versão nova', async () => {
    put.mockRejectedValue(new ApiError(409, 'As regras foram alteradas por outra pessoa', 'precificacao_versao'))
    const w = await setup()
    get.mockResolvedValue(rules(6, 6.5))
    await w.find('input[inputmode="decimal"]').setValue('14')
    await button(w, 'Salvar regras').trigger('click')
    await flushPromises()
    expect(w.text()).toContain('alteradas por outra pessoa')
    expect(w.text()).toContain('Versão 6')
    expect(get).toHaveBeenCalledTimes(2)
    // As edições locais foram descartadas pela recarga.
    expect(w.text()).not.toContain('Salvar regras')
  })

  it('calcula a prévia pelo endpoint', async () => {
    const w = await setup()
    get.mockResolvedValueOnce({ precoUsd: 12, precoPixBrl: 72, multiplicadorFaixa: 6, precoPelaFaixaBrl: 72, pisoBrl: 60, ajustadoPeloPiso: false, versao: 4 })
    await w.find('input[aria-label="Preço em dólar"]').setValue('12')
    await w.findAll('form')[0]!.trigger('submit')
    await flushPromises()
    expect(get).toHaveBeenLastCalledWith('/api/precificacao/calculo', { usd: 12 })
    expect(w.find('[data-testid="calc-final"]').text()).toBe('R$ 72,00')
  })

  it('reenviar chama o endpoint e recarrega', async () => {
    post.mockResolvedValue(undefined)
    const w = await setup()
    await button(w, 'Reenviar agora').trigger('click')
    await flushPromises()
    expect(post).toHaveBeenCalledWith('/api/precificacao/reenviar')
  })

  it('importar pede confirmação e usa forcar quando já existe versão', async () => {
    post.mockResolvedValue(rules(5))
    const w = await setup()
    await button(w, 'Importar do crivonft').trigger('click')
    await flushPromises()
    expect(post).toHaveBeenCalledWith('/api/precificacao/importar-crivonft', { forcar: true })
  })
})
