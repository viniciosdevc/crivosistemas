import { describe, expect, it } from 'vitest'
import { habboAvatarUrl } from '@/lib/habbo'

describe('habboAvatarUrl', () => {
  it('monta a URL da cabeça por padrão', () => {
    expect(habboAvatarUrl('ana')).toBe('https://www.habbo.com.br/habbo-imaging/avatarimage?size=b&user=ana&direction=2&head_direction=2&headonly=1')
  })
  it('codifica o nickname e aceita opções', () => {
    const url = habboAvatarUrl(' a b&c ', { size: 'l', headOnly: false })
    expect(url).toBe('https://www.habbo.com.br/habbo-imaging/avatarimage?size=l&user=a%20b%26c&direction=2&head_direction=2')
  })
  it('devolve null sem nickname', () => {
    expect(habboAvatarUrl('')).toBeNull()
    expect(habboAvatarUrl('  ')).toBeNull()
    expect(habboAvatarUrl(undefined)).toBeNull()
  })
})
