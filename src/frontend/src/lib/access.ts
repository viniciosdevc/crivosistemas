import { countryCentroids } from './countryCentroids'
import type { AcessoLocal } from './types'

export interface CountryTotal { pais: string; nome: string; acessos: number }
export interface MapPoint { lat: number; lng: number; acessos: number; label: string; pais: string }

const names = new Intl.DisplayNames(['pt-BR'], { type: 'region' })

export function countryName(code: string) {
  try {
    return names.of(code) ?? code
  } catch {
    return code
  }
}

/** "Campinas, São Paulo, Brasil" — só o que existir. */
export function placeLabel(l: { pais: string | null; regiao?: string | null; cidade?: string | null }) {
  return [l.cidade, l.regiao, l.pais ? countryName(l.pais) : null].filter(Boolean).join(', ')
}

export function totalsByCountry(locais: AcessoLocal[]): CountryTotal[] {
  const map = new Map<string, number>()
  for (const l of locais) map.set(l.pais, (map.get(l.pais) ?? 0) + l.acessos)
  return [...map.entries()]
    .map(([pais, acessos]) => ({ pais, nome: countryName(pais), acessos }))
    .sort((a, b) => b.acessos - a.acessos)
}

/** Um ponto por cidade; sem coordenadas, cai no centro do país (somando as cidades sem posição). */
export function toMapPoints(locais: AcessoLocal[]): MapPoint[] {
  const points: MapPoint[] = []
  const fallback = new Map<string, number>()
  for (const l of locais) {
    if (l.latitude != null && l.longitude != null) {
      points.push({ lat: l.latitude, lng: l.longitude, acessos: l.acessos, label: placeLabel(l), pais: l.pais })
    } else {
      fallback.set(l.pais, (fallback.get(l.pais) ?? 0) + l.acessos)
    }
  }
  for (const [pais, acessos] of fallback) {
    const c = countryCentroids[pais]
    if (c) points.push({ lat: c[0], lng: c[1], acessos, label: `${countryName(pais)} (sem cidade)`, pais })
  }
  return points
}

/** Destaque 0..1 de um valor entre 0 e o máximo, em escala de raiz para os menores não sumirem. */
export function heat(value: number, max: number) {
  return max > 0 ? Math.sqrt(value / max) : 0
}
