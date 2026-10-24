import { beforeEach, describe, expect, it, vi } from 'vitest'
import { carregarImagens, imagemDoItem, limparCacheImagens, nomeBaseItem } from '@/lib/imagens'

const get = vi.fn()
vi.mock('@/lib/api', () => ({ api: { get: (...a: unknown[]) => get(...a) } }))

describe('nomeBaseItem', () => {
  it('remove o sufixo +N e trata vazio', () => {
    expect(nomeBaseItem('Dragão Azul +3')).toBe('Dragão Azul')
    expect(nomeBaseItem('Dragão')).toBe('Dragão')
    expect(nomeBaseItem(null)).toBeNull()
  })
})

describe('carregarImagens', () => {
  beforeEach(() => {
    get.mockReset()
    limparCacheImagens()
  })

  it('pede só os nomes ainda não vistos, em um único pedido, sem o sufixo', async () => {
    get.mockResolvedValue({ imagens: { A: 'https://x/a.png', B: null } })
    await carregarImagens(['A +2', 'B', 'A', null])
    expect(get).toHaveBeenCalledTimes(1)
    expect(get.mock.calls[0]![0]).toBe('/api/vendas/imagens?nome=A&nome=B')
    expect(imagemDoItem('A +1')).toBe('https://x/a.png')
    expect(imagemDoItem('B')).toBeNull()

    await carregarImagens(['A', 'B'])
    expect(get).toHaveBeenCalledTimes(1)
    get.mockResolvedValue({ imagens: { C: 'https://x/c.png' } })
    await carregarImagens(['A', 'C'])
    expect(get).toHaveBeenCalledTimes(2)
    expect(get.mock.calls[1]![0]).toBe('/api/vendas/imagens?nome=C')
  })

  it('divide em pedidos de até 50 nomes e tolera falha', async () => {
    get.mockResolvedValue({ imagens: {} })
    await carregarImagens(Array.from({ length: 120 }, (_, i) => `item${i}`))
    expect(get).toHaveBeenCalledTimes(3)
    get.mockRejectedValue(new Error('x'))
    await expect(carregarImagens(['novo'])).resolves.toBeUndefined()
    expect(imagemDoItem('novo')).toBeNull()
  })
})
