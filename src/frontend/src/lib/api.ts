/** Central HTTP client. Every call is same-origin with the session cookie; errors are normalised into ApiError. */

export class ApiError extends Error {
  readonly status: number
  readonly code: string | undefined
  /** Field errors keyed by camelCase field name, as returned by the API's validation. */
  readonly fields: Record<string, string>

  constructor(status: number, message: string, code?: string, fields: Record<string, string> = {}) {
    super(message)
    this.status = status
    this.code = code
    this.fields = fields
  }
}

type Query = Record<string, string | number | boolean | null | undefined>

interface RequestOptions {
  query?: Query
  body?: unknown
  idempotencyKey?: string
  signal?: AbortSignal
}

let onUnauthorized: (() => void) | null = null

/** Registered by the auth store: a 401 on a protected call means the session ended. */
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler
}

function buildUrl(path: string, query?: Query) {
  if (!query) return path
  const params = new URLSearchParams()
  for (const [k, v] of Object.entries(query)) {
    if (v !== undefined && v !== null && v !== '') params.set(k, String(v))
  }
  const qs = params.toString()
  return qs ? `${path}?${qs}` : path
}

function camel(key: string) {
  return key.replace(/^[A-Z]/, (c) => c.toLowerCase()).replace(/\.([A-Z])/g, (_, c: string) => '.' + c.toLowerCase())
}

async function request<T>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', 'X-Requested-With': 'crivosistemas' }
  if (options.body !== undefined) headers['Content-Type'] = 'application/json'
  if (options.idempotencyKey) headers['Idempotency-Key'] = options.idempotencyKey

  let response: Response
  try {
    response = await fetch(buildUrl(path, options.query), {
      method,
      headers,
      credentials: 'same-origin',
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    })
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') throw e
    throw new ApiError(0, 'Sem conexão. Verifique sua internet e tente novamente.', 'network')
  }

  if (response.status === 204) return undefined as T
  const data = response.headers.get('content-type')?.includes('json') ? await response.json().catch(() => null) : null

  if (!response.ok) {
    if (response.status === 401 && !path.startsWith('/api/auth/')) onUnauthorized?.()
    const fields: Record<string, string> = {}
    if (data?.errors && typeof data.errors === 'object') {
      for (const [k, v] of Object.entries(data.errors as Record<string, string[]>)) fields[camel(k)] = v[0] ?? ''
    }
    const message =
      response.status === 429
        ? 'Muitas tentativas em pouco tempo. Aguarde um instante.'
        : Object.keys(fields).length > 0
          ? (data?.title && !String(data.title).startsWith('One or more') ? data.title : 'Verifique os campos destacados.')
          : (data?.title ?? (response.status >= 500 ? 'Servidor indisponível no momento. Tente novamente em instantes.' : 'Não foi possível concluir a operação.'))
    throw new ApiError(response.status, message, data?.code, fields)
  }
  return data as T
}

export const api = {
  get: <T>(path: string, query?: Query, signal?: AbortSignal) => request<T>('GET', path, { query, signal }),
  post: <T>(path: string, body?: unknown, options: Omit<RequestOptions, 'body'> = {}) => request<T>('POST', path, { ...options, body: body ?? {} }),
  put: <T>(path: string, body?: unknown) => request<T>('PUT', path, { body: body ?? {} }),
  delete: <T = void>(path: string) => request<T>('DELETE', path),
}

/** Key that makes a create request safe to retry (double click, flaky network). Keep one per form submission intent. */
export function newIdempotencyKey() {
  return crypto.randomUUID()
}
