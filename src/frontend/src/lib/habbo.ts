/** URL do avatar do Habbo Brasil a partir do nickname. Sem nickname devolve null (o avatar cai nas iniciais). */
export function habboAvatarUrl(nick: string | null | undefined, opts: { size?: 's' | 'm' | 'b' | 'l'; headOnly?: boolean } = {}): string | null {
  const nickname = nick?.trim()
  if (!nickname) return null
  const { size = 'b', headOnly = true } = opts
  const head = headOnly ? '&headonly=1' : ''
  return `https://www.habbo.com.br/habbo-imaging/avatarimage?size=${size}&user=${encodeURIComponent(nickname)}&direction=2&head_direction=2${head}`
}
