/** Passkeys (WebAuthn) no navegador. O servidor (Fido2NetLib) fala bytes em base64url; o navegador fala ArrayBuffer. */
import { api } from '@/lib/api'
import type { Me } from '@/lib/types'

export interface PasskeyItem {
  id: string
  name: string
  createdAt: string
  lastUsedAt: string | null
}

interface DescriptorJson { id: string; type: string; transports?: string[] }
interface CreationOptionsJson {
  rp: PublicKeyCredentialRpEntity
  user: { id: string; name: string; displayName: string }
  challenge: string
  pubKeyCredParams: PublicKeyCredentialParameters[]
  timeout?: number
  excludeCredentials?: DescriptorJson[]
  authenticatorSelection?: AuthenticatorSelectionCriteria
  attestation?: AttestationConveyancePreference
}
interface RequestOptionsJson {
  challenge: string
  timeout?: number
  rpId?: string
  allowCredentials?: DescriptorJson[]
  userVerification?: UserVerificationRequirement
}

export function passkeysSupported() {
  return typeof window !== 'undefined' && !!window.PublicKeyCredential && !!navigator.credentials
}

function fromB64Url(value: string): ArrayBuffer {
  const b64 = value.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(value.length / 4) * 4, '=')
  const bin = atob(b64)
  const bytes = new Uint8Array(bin.length)
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i)
  return bytes.buffer
}

function toB64Url(buffer: ArrayBuffer): string {
  let bin = ''
  for (const b of new Uint8Array(buffer)) bin += String.fromCharCode(b)
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

const descriptors = (list?: DescriptorJson[]): PublicKeyCredentialDescriptor[] =>
  (list ?? []).map((d) => ({ type: 'public-key', id: fromB64Url(d.id), transports: d.transports as AuthenticatorTransport[] | undefined }))

/** Traduz o erro do navegador (cancelou, não há chave, etc.) para uma mensagem em português. */
export function passkeyErrorMessage(e: unknown, fallback: string) {
  if (e instanceof DOMException) {
    if (e.name === 'NotAllowedError') return 'A operação foi cancelada ou expirou.'
    if (e.name === 'InvalidStateError') return 'Este dispositivo já tem uma chave cadastrada para a sua conta.'
    if (e.name === 'SecurityError') return 'Este navegador não permite chaves de acesso neste endereço.'
  }
  return fallback
}

export async function registerPasskey(name: string): Promise<PasskeyItem> {
  const options = await api.post<CreationOptionsJson>('/api/auth/passkeys/options')
  const credential = (await navigator.credentials.create({
    publicKey: {
      rp: options.rp,
      user: { id: fromB64Url(options.user.id), name: options.user.name, displayName: options.user.displayName },
      challenge: fromB64Url(options.challenge),
      pubKeyCredParams: options.pubKeyCredParams,
      timeout: options.timeout,
      excludeCredentials: descriptors(options.excludeCredentials),
      authenticatorSelection: options.authenticatorSelection,
      attestation: options.attestation,
    },
  })) as PublicKeyCredential | null
  if (!credential) throw new Error('cancelled')
  const response = credential.response as AuthenticatorAttestationResponse
  return api.post<PasskeyItem>('/api/auth/passkeys', {
    name,
    credential: {
      id: credential.id,
      rawId: toB64Url(credential.rawId),
      type: 'public-key',
      response: { attestationObject: toB64Url(response.attestationObject), clientDataJSON: toB64Url(response.clientDataJSON) },
    },
  })
}

export async function loginWithPasskey(): Promise<Me> {
  const { challengeId, options } = await api.post<{ challengeId: string; options: RequestOptionsJson }>('/api/auth/passkey-login/options')
  const credential = (await navigator.credentials.get({
    publicKey: {
      challenge: fromB64Url(options.challenge),
      timeout: options.timeout,
      rpId: options.rpId,
      allowCredentials: descriptors(options.allowCredentials),
      userVerification: options.userVerification,
    },
  })) as PublicKeyCredential | null
  if (!credential) throw new Error('cancelled')
  const response = credential.response as AuthenticatorAssertionResponse
  return api.post<Me>('/api/auth/passkey-login', {
    challengeId,
    credential: {
      id: credential.id,
      rawId: toB64Url(credential.rawId),
      type: 'public-key',
      response: {
        authenticatorData: toB64Url(response.authenticatorData),
        clientDataJSON: toB64Url(response.clientDataJSON),
        signature: toB64Url(response.signature),
        userHandle: response.userHandle ? toB64Url(response.userHandle) : null,
      },
    },
  })
}
