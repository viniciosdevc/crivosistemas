import { describe, expect, it } from 'vitest'
import { heat, onlineToMapPoints, pageLabel, toMapPoints, totalsByCountry } from '@/lib/access'
import type { AcessoLocal } from '@/lib/types'

const locais: AcessoLocal[] = [
  { pais: 'BR', regiao: 'São Paulo', cidade: 'Campinas', latitude: -22.9, longitude: -47.06, acessos: 10 },
  { pais: 'BR', regiao: '', cidade: '', latitude: null, longitude: null, acessos: 4 },
  { pais: 'PT', regiao: '', cidade: '', latitude: null, longitude: null, acessos: 2 },
]

describe('acessos por local', () => {
  it('soma por país em ordem decrescente', () => {
    expect(totalsByCountry(locais).map((c) => [c.pais, c.acessos])).toEqual([['BR', 14], ['PT', 2]])
  })
  it('usa o centro do país quando não há coordenadas', () => {
    const pts = toMapPoints(locais)
    expect(pts).toHaveLength(3)
    expect(pts.find((p) => p.pais === 'PT')?.acessos).toBe(2)
    expect(pts.find((p) => p.label.startsWith('Campinas'))?.lat).toBe(-22.9)
  })
  it('heat vai de 0 a 1', () => {
    expect(heat(0, 10)).toBe(0)
    expect(heat(10, 10)).toBe(1)
    expect(heat(1, 0)).toBe(0)
  })
  it('posiciona quem está online e nomeia a página', () => {
    const pts = onlineToMapPoints([{ pais: 'PT', regiao: '', cidade: '', latitude: null, longitude: null, qtd: 3 }])
    expect(pts[0]?.acessos).toBe(3)
    expect(pageLabel('/clothes/Cartola', { clothes: 'Roupas' })).toBe('Cartola (Roupas)')
    expect(pageLabel('/ranking', {})).toBe('/ranking')
  })
})
