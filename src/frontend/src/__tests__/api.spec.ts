import { afterEach, describe, expect, it, vi } from 'vitest'
import { api, ApiError } from '@/lib/api'

function respond(status: number, body: unknown) {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })))
}

async function failure(call: Promise<unknown>): Promise<ApiError> {
  try {
    await call
  } catch (e) {
    return e as ApiError
  }
  throw new Error('esperava erro')
}

describe('api: mensagens de erro', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('mostra o detail do 502 do crivonft em vez de um texto genérico', async () => {
    respond(502, { title: 'x', detail: 'O crivonft ainda não tem a rota de integração; publique a atualização da loja.', code: 'crivonft_indisponivel' })
    const e = await failure(api.post('/api/precificacao/importar-crivonft'))
    expect(e).toBeInstanceOf(ApiError)
    expect(e.status).toBe(502)
    expect(e.message).toContain('publique a atualização da loja')
  })

  it('o 503 do nginx continua com a frase de atualização', async () => {
    respond(503, { title: 'Estamos atualizando o sistema. Tente novamente em instantes.', code: 'updating' })
    const e = await failure(api.get('/api/vendas'))
    expect(e.message).toBe('Estamos atualizando o sistema. Tente novamente em instantes.')
    expect(e.code).toBe('updating')
  })
})
