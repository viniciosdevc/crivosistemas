import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import UsersView from '@/views/admin/users/UsersView.vue'
import type { AppUser } from '@/lib/types'

const get = vi.fn()
const post = vi.fn()
vi.mock('@/lib/api', async (orig) => ({
  ...(await orig<typeof import('@/lib/api')>()),
  api: { get: (...a: unknown[]) => get(...a), post: (...a: unknown[]) => post(...a), put: vi.fn() },
}))
vi.mock('@/lib/confirm', () => ({ confirm: vi.fn().mockResolvedValue(true) }))

const users: AppUser[] = [
  { id: 'u1', name: 'Ana Admin', email: 'ana@x.com', role: 'admin', status: 'active', createdAt: '2026-10-01T10:00:00Z', lastLoginAt: '2026-10-22T10:00:00Z', isYou: true },
  { id: 'u2', name: 'Beto Operador', email: 'beto@x.com', role: 'operator', status: 'disabled', createdAt: '2026-10-02T10:00:00Z', lastLoginAt: null, isYou: false },
]

describe('UsersView', () => {
  beforeEach(() => {
    get.mockReset()
    post.mockReset()
    get.mockResolvedValue({ items: users, page: 1, pageSize: 20, total: 2 })
    // O ResizeObserver não existe no jsdom e os componentes do Reka UI o usam.
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} })
  })

  it('lista usuários com papel e situação', async () => {
    const wrapper = mount(UsersView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    expect(get).toHaveBeenCalledWith('/api/users', { search: '', page: 1, pageSize: 20 }, expect.anything())
    const text = wrapper.text()
    expect(text).toContain('Ana Admin')
    expect(text).toContain('(você)')
    expect(text).toContain('Administrador')
    expect(text).toContain('Beto Operador')
    expect(text).toContain('Operador')
    expect(text).toContain('Desativado')
    expect(text).toContain('Nunca entrou')
  })

  it('tem o botão de novo usuário', async () => {
    const wrapper = mount(UsersView, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    expect(wrapper.text()).toContain('Novo usuário')
  })
})
