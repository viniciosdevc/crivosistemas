import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api } from '@/lib/api'
import { loginWithPasskey } from '@/lib/passkey'
import type { Me } from '@/lib/types'

export const useAuthStore = defineStore('auth', () => {
  const me = ref<Me | null>(null)
  const loaded = ref(false)
  const isAdmin = computed(() => me.value?.role === 'admin')

  function setMe(value: Me | null) {
    me.value = value
    loaded.value = true
  }

  async function load() {
    try {
      setMe(await api.get<Me>('/api/auth/me'))
    } catch {
      setMe(null)
    }
  }

  async function login(email: string, password: string) {
    setMe(await api.post<Me>('/api/auth/login', { email, password }))
  }

  async function loginPasskey() {
    setMe(await loginWithPasskey())
  }

  async function logout() {
    await api.post('/api/auth/logout').catch(() => undefined)
    me.value = null
  }

  /** Chamado quando a API informa que a sessão acabou. */
  function expire() {
    me.value = null
  }

  return { me, loaded, isAdmin, load, login, loginPasskey, logout, expire }
})
