import { api } from '@/lib/api'
import type { ClienteLista } from '@/lib/types'

/** Edita carteira (texto livre; vazio remove) e/ou favorito. O servidor decide e devolve o cliente como ficou. */
export function atualizarCliente(id: string, patch: { carteira?: string | null; favorito?: boolean }) {
  return api.patch<ClienteLista>(`/api/clientes/${id}`, patch)
}
