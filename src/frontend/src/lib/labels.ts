export type Tone = 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'brand'

export const userRole: Record<string, { label: string; tone: Tone }> = {
  admin: { label: 'Administrador', tone: 'brand' },
  operator: { label: 'Operador', tone: 'neutral' },
}

export const userStatus: Record<string, { label: string; tone: Tone }> = {
  active: { label: 'Ativo', tone: 'success' },
  disabled: { label: 'Desativado', tone: 'danger' },
}
