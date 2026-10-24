import { reactive } from 'vue'
import { api } from './api'
import type { ImagensItens } from './types'

/** Cache em memória do módulo: nome do item → URL da imagem (ou null quando não há). */
const cache = reactive<Record<string, string | null>>({})
const pending = new Set<string>()
const MAX_POR_PEDIDO = 50

/** Tira o sufixo " +N" que a lista acrescenta quando a venda tem mais de um item. */
export function nomeBaseItem(item: string | null | undefined): string | null {
  const nome = item?.replace(/\s\+\d+$/, '').trim()
  return nome ? nome : null
}

/** Busca em lote as imagens dos nomes ainda não vistos (um pedido por conjunto, em partes de 50). Falhas não quebram a tela. */
export async function carregarImagens(itens: (string | null | undefined)[]): Promise<void> {
  const nomes = [...new Set(itens.map(nomeBaseItem).filter((n): n is string => !!n))].filter((n) => !(n in cache) && !pending.has(n))
  for (let i = 0; i < nomes.length; i += MAX_POR_PEDIDO) {
    const parte = nomes.slice(i, i + MAX_POR_PEDIDO)
    parte.forEach((n) => pending.add(n))
    const params = new URLSearchParams()
    parte.forEach((n) => params.append('nome', n))
    try {
      const res = await api.get<ImagensItens>(`/api/vendas/imagens?${params.toString()}`)
      for (const n of parte) cache[n] = res.imagens?.[n] ?? null
    } catch {
      // Sem imagem por enquanto: a tabela mostra o placeholder e tenta de novo na próxima carga.
    } finally {
      parte.forEach((n) => pending.delete(n))
    }
  }
}

/** URL da imagem já carregada para o item (reativa), ou null. */
export function imagemDoItem(item: string | null | undefined): string | null {
  const nome = nomeBaseItem(item)
  return nome ? (cache[nome] ?? null) : null
}

/** Esvazia o cache (testes). */
export function limparCacheImagens() {
  for (const k of Object.keys(cache)) delete cache[k]
  pending.clear()
}
