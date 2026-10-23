import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/lib/api'
import { useAuthStore } from '@/stores/auth'

const get = vi.fn()
const post = vi.fn()
vi.mock('@/lib/api', async (orig) => ({
  ...(await orig<typeof import('@/lib/api')>()),
  api: { get: (...a: unknown[]) => get(...a), post: (...a: unknown[]) => post(...a) },
}))

describe('auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    get.mockReset()
    post.mockReset()
  })

  it('carrega o usuário logado e expõe o papel', async () => {
    get.mockResolvedValue({ id: '1', name: 'Ana', email: 'ana@x.com', role: 'admin' })
    const auth = useAuthStore()
    await auth.load()
    expect(auth.loaded).toBe(true)
    expect(auth.me?.email).toBe('ana@x.com')
    expect(auth.isAdmin).toBe(true)
  })

  it('operador não é admin', async () => {
    post.mockResolvedValue({ id: '2', name: 'Beto', email: 'beto@x.com', role: 'operator' })
    const auth = useAuthStore()
    await auth.login('beto@x.com', 'senha-segura-1')
    expect(post).toHaveBeenCalledWith('/api/auth/login', { email: 'beto@x.com', password: 'senha-segura-1' })
    expect(auth.isAdmin).toBe(false)
  })

  it('sessão inválida deixa me nulo mas marca como carregado', async () => {
    get.mockRejectedValue(new ApiError(401, 'Não autenticado'))
    const auth = useAuthStore()
    await auth.load()
    expect(auth.loaded).toBe(true)
    expect(auth.me).toBeNull()
  })

  it('logout limpa o usuário mesmo se a chamada falhar', async () => {
    post.mockRejectedValue(new ApiError(500, 'erro'))
    const auth = useAuthStore()
    auth.me = { id: '1', name: 'Ana', email: 'ana@x.com', role: 'admin' }
    await auth.logout()
    expect(auth.me).toBeNull()
  })
})
