import { ref, shallowRef } from 'vue'
import { ApiError } from './api'

/** Loads data with loading/error state. Keeps the previous data while reloading, so tables don't jump. */
export function useAsync<T>(loader: (signal: AbortSignal) => Promise<T>) {
  const data = shallowRef<T | null>(null)
  const loading = ref(false)
  const error = ref<ApiError | null>(null)
  let controller: AbortController | null = null

  async function run() {
    controller?.abort()
    const current = (controller = new AbortController())
    loading.value = true
    error.value = null
    try {
      const result = await loader(current.signal)
      if (current === controller) data.value = result
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return
      if (current === controller) error.value = e instanceof ApiError ? e : new ApiError(0, 'Erro inesperado.')
    } finally {
      if (current === controller) loading.value = false
    }
  }

  return { data, loading, error, run }
}
