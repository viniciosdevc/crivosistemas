import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import CarteiraEditor from '@/components/admin/clientes/CarteiraEditor.vue'
import FavoritoEstrela from '@/components/admin/clientes/FavoritoEstrela.vue'
import CustomersView from '@/views/admin/CustomersView.vue'
import { ApiError } from '@/lib/api'
import { toasts } from '@/lib/toast'
import type { ClienteLista } from '@/lib/types'

const get = vi.fn()
const patch = vi.fn()
vi.mock('@/lib/api', async (orig) => ({
  ...(await orig<typeof import('@/lib/api')>()),
  api: { get: (...a: unknown[]) => get(...a), patch: (...a: unknown[]) => patch(...a) },
}))

const ENDERECO = '0x' + 'a'.repeat(40)
const base: ClienteLista = {
  id: 'c1', nickname: 'Ana', email: null, carteira: null, carteiraValida: false, carteiraLoja: null, favorito: false, totalGasto: 100, compras: 2, ultimaCompra: '2026-10-20T10:00:00Z',
}

beforeEach(() => {
  get.mockReset()
  patch.mockReset()
  toasts.splice(0)
  vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} })
})

describe('FavoritoEstrela', () => {
  it('liga na hora (otimista) e confirma com a resposta do servidor', async () => {
    let resolver: (c: ClienteLista) => void = () => {}
    patch.mockReturnValue(new Promise<ClienteLista>((r) => (resolver = r)))
    const w = mount(FavoritoEstrela, { props: { id: 'c1', favorito: false, nickname: 'Ana' } })
    const botao = w.get('button')
    expect(botao.attributes('aria-pressed')).toBe('false')
    await botao.trigger('click')
    expect(patch).toHaveBeenCalledWith('/api/clientes/c1', { favorito: true })
    expect(botao.attributes('aria-pressed')).toBe('true') // antes da resposta
    resolver({ ...base, favorito: true })
    await flushPromises()
    expect(w.emitted('changed')?.[0]?.[0]).toMatchObject({ favorito: true })
    expect(botao.attributes('aria-pressed')).toBe('true')
  })

  it('volta atrás e mostra o motivo quando o servidor recusa', async () => {
    patch.mockRejectedValue(new ApiError(502, 'O crivonft ainda não tem a rota de integração; publique a atualização da loja.'))
    const w = mount(FavoritoEstrela, { props: { id: 'c1', favorito: false } })
    await w.get('button').trigger('click')
    await flushPromises()
    expect(w.get('button').attributes('aria-pressed')).toBe('false')
    expect(w.emitted('changed')).toBeUndefined()
    expect(toasts.at(-1)).toMatchObject({ tone: 'error', description: expect.stringContaining('publique a atualização') })
  })
})

describe('CarteiraEditor', () => {
  it('vazia: oferece "Adicionar carteira", salva o texto e avisa o pai', async () => {
    patch.mockResolvedValue({ ...base, carteira: 'carteira da Ana' })
    const w = mount(CarteiraEditor, { props: { cliente: base } })
    expect(w.text()).toContain('Adicionar carteira')
    await w.get('button').trigger('click')
    await w.get('input').setValue('  carteira da Ana ')
    await w.get('form').trigger('submit')
    await flushPromises()
    expect(patch).toHaveBeenCalledWith('/api/clientes/c1', { carteira: 'carteira da Ana' })
    expect(w.emitted('changed')?.[0]?.[0]).toMatchObject({ carteira: 'carteira da Ana' })
  })

  it('válida: mostra copiar e link do explorer; inválida: selo e sem link', () => {
    const valida = mount(CarteiraEditor, { props: { cliente: { ...base, carteira: ENDERECO, carteiraValida: true }, full: true } })
    expect(valida.find('a[href*="explorer"]').exists()).toBe(true)
    expect(valida.find('button[aria-label="Copiar carteira"]').exists()).toBe(true)
    expect(valida.text()).not.toContain('formato inválido')

    const invalida = mount(CarteiraEditor, { props: { cliente: { ...base, carteira: '0x123', carteiraValida: false }, full: true } })
    expect(invalida.text()).toContain('formato inválido')
    expect(invalida.text()).toContain('0x123')
    expect(invalida.find('a').exists()).toBe(false)
    expect(invalida.find('button[aria-label="Copiar carteira"]').exists()).toBe(false)
  })

  it('limpar remove a carteira (null) e erro do servidor mantém o campo aberto', async () => {
    patch.mockResolvedValueOnce({ ...base, carteira: null })
    const w = mount(CarteiraEditor, { props: { cliente: { ...base, carteira: 'x', carteiraValida: false } } })
    await w.get('button[aria-label="Editar carteira"]').trigger('click')
    const limpar = w.findAll('button').find((b) => b.text() === 'Limpar')!
    await limpar.trigger('click')
    await flushPromises()
    expect(patch).toHaveBeenCalledWith('/api/clientes/c1', { carteira: null })

    patch.mockRejectedValueOnce(new ApiError(502, 'Token ou segredo diferentes nos dois sistemas.'))
    await w.get('button[aria-label="Editar carteira"]').trigger('click')
    await w.get('input').setValue('nova')
    await w.get('form').trigger('submit')
    await flushPromises()
    expect(w.find('input').exists()).toBe(true)
    expect(toasts.at(-1)?.description).toContain('Token ou segredo')
  })

  it('mostra a carteira vinculada na loja numa segunda linha quando existe', () => {
    const w = mount(CarteiraEditor, { props: { cliente: { ...base, carteira: 'texto', carteiraLoja: ENDERECO } } })
    expect(w.text()).toContain('Carteira vinculada na loja')
  })
})

describe('CustomersView: filtros na URL', () => {
  async function setup(url: string) {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/painel/clientes', name: 'customers', component: CustomersView },
        { path: '/painel/clientes/:id', name: 'customer', component: { render: () => null } },
      ],
    })
    await router.push(url)
    const wrapper = mount(CustomersView, { global: { plugins: [router] } })
    await flushPromises()
    return { router, wrapper }
  }

  beforeEach(() => get.mockResolvedValue({ items: [base], page: 1, pageSize: 20, total: 1 }))

  it('aplica na API os filtros que vêm da URL', async () => {
    await setup('/painel/clientes?favorito=1&carteira=sem')
    expect(get).toHaveBeenCalledWith('/api/clientes', expect.objectContaining({ favorito: true, carteira: 'sem' }), expect.anything())
  })

  it('os chips escrevem na URL, "com" e "sem" se excluem e clicar de novo desliga', async () => {
    const { router, wrapper } = await setup('/painel/clientes')
    const chip = (t: string) => wrapper.findAll('button[aria-pressed]').find((b) => b.text() === t)!
    await chip('Favoritos').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query).toMatchObject({ favorito: '1' })
    await chip('Com carteira').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query).toMatchObject({ favorito: '1', carteira: 'com' })
    await chip('Sem carteira').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.carteira).toBe('sem')
    await chip('Sem carteira').trigger('click')
    await chip('Favoritos').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.carteira).toBeUndefined()
    expect(router.currentRoute.value.query.favorito).toBeUndefined()
    expect(get).toHaveBeenLastCalledWith('/api/clientes', expect.objectContaining({ favorito: undefined, carteira: undefined }), expect.anything())
  })

  it('a estrela da linha atualiza o favorito sem recarregar a lista', async () => {
    patch.mockResolvedValue({ ...base, favorito: true })
    const { wrapper } = await setup('/painel/clientes')
    const chamadas = get.mock.calls.length
    await wrapper.get('button[aria-label^="Favoritar"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('button[aria-label^="Remover"]').exists()).toBe(true)
    expect(get.mock.calls.length).toBe(chamadas)
  })
})
