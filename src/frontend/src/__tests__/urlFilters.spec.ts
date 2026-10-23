import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { buildQuery, parseFilters, useUrlFilters } from '@/lib/urlFilters'

const defaults = { origem: '', busca: '', ordem: 'data', page: '1' }

describe('filtros na URL', () => {
  it('lê só as chaves conhecidas e usa o padrão no resto', () => {
    expect(parseFilters({ origem: 'nft', page: '3', outra: 'x' }, defaults)).toEqual({ origem: 'nft', busca: '', ordem: 'data', page: '3' })
    expect(parseFilters({ origem: ['site', 'nft'], busca: '  ' }, defaults)).toEqual({ ...defaults, origem: 'site' })
  })

  it('escreve só o que difere do padrão', () => {
    expect(buildQuery({ origem: 'nft', busca: '', ordem: 'data', page: '1' }, defaults)).toEqual({ origem: 'nft' })
    expect(buildQuery({ origem: '', busca: 'ana', ordem: 'total', page: '2' }, defaults)).toEqual({ busca: 'ana', ordem: 'total', page: '2' })
  })

  async function setup(url: string) {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/lista', name: 'lista', component: { render: () => null } }, { path: '/outra', name: 'outra', component: { render: () => null } }],
    })
    await router.push(url)
    let api!: ReturnType<typeof useUrlFilters<typeof defaults>>
    mount(defineComponent({ setup() { api = useUrlFilters(defaults); return () => h('div') } }), { global: { plugins: [router] } })
    return { router, api }
  }

  it('começa pelos valores da URL', async () => {
    const { api } = await setup('/lista?origem=cambios&page=2')
    expect(api.filters.value).toMatchObject({ origem: 'cambios', page: '2' })
  })

  it('mudar um filtro atualiza a URL e volta para a página 1', async () => {
    const { router, api } = await setup('/lista?page=4')
    api.set({ origem: 'nft' })
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({ origem: 'nft' })
    expect(api.filters.value.page).toBe('1')
  })

  it('mudar só a página mantém os filtros', async () => {
    const { router, api } = await setup('/lista?busca=ana')
    api.set({ page: '3' })
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({ busca: 'ana', page: '3' })
  })

  it('reage ao botão voltar (query mudou de fora)', async () => {
    const { router, api } = await setup('/lista?origem=nft')
    await router.push('/lista?origem=site')
    await flushPromises()
    expect(api.filters.value.origem).toBe('site')
  })

  it('ignora a navegação para outra tela', async () => {
    const { router, api } = await setup('/lista?origem=nft')
    await router.push('/outra')
    await flushPromises()
    expect(api.filters.value.origem).toBe('nft')
  })

  it('limpar volta ao padrão', async () => {
    const { router, api } = await setup('/lista?origem=nft&busca=x')
    api.reset()
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({})
    expect(api.filters.value).toEqual(defaults)
  })
})
