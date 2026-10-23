import { ref, watch } from 'vue'
import { useRoute, useRouter, type LocationQuery } from 'vue-router'

export type FilterValues = Record<string, string>

/** Lê os filtros da query string: só chaves conhecidas, texto não vazio; o resto cai no padrão. */
export function parseFilters<T extends FilterValues>(query: LocationQuery, defaults: T): T {
  const out: FilterValues = { ...defaults }
  for (const key of Object.keys(defaults)) {
    const raw = query[key]
    const value = Array.isArray(raw) ? raw[0] : raw
    if (typeof value === 'string' && value.trim() !== '') out[key] = value.trim()
  }
  return out as T
}

/** Monta a query só com o que difere do padrão, para a URL ficar curta e compartilhável. */
export function buildQuery(values: FilterValues, defaults: FilterValues) {
  const query: Record<string, string> = {}
  for (const [key, value] of Object.entries(values)) {
    if (value !== '' && value !== defaults[key]) query[key] = value
  }
  return query
}

/**
 * Filtros de uma lista refletidos na URL (voltar/avançar e links compartilhados funcionam).
 * Qualquer mudança que não seja de `page` volta para a página 1. Ignora mudanças de rota depois que a tela saiu de cena.
 * `defaults` deve conter `page: '1'` quando a lista pagina.
 */
export function useUrlFilters<T extends FilterValues>(defaults: T) {
  const route = useRoute()
  const router = useRouter()
  const name = route.name
  const filters = ref(parseFilters(route.query, defaults))

  watch(
    () => route.query,
    (query) => {
      if (route.name === name) filters.value = parseFilters(query, defaults)
    },
  )

  function set(patch: Partial<T>) {
    const next = { ...filters.value, ...patch } as FilterValues
    if (!('page' in patch) && 'page' in defaults) next.page = defaults.page!
    const query = buildQuery(next, defaults)
    // Atualiza já, sem esperar a navegação, para a tela reagir de imediato.
    filters.value = parseFilters(query, defaults)
    void router.replace({ query })
  }

  function reset() {
    set({ ...defaults })
  }

  return { filters, set, reset }
}
