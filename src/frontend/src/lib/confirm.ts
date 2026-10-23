import { reactive } from 'vue'

export const confirmState = reactive({
  open: false,
  title: '',
  description: '' as string | undefined,
  confirmLabel: 'Confirmar',
  cancelLabel: 'Voltar',
  danger: false,
  /** Rótulo do campo de motivo (vazio = sem campo). */
  reasonLabel: '' as string,
  reason: '',
})

let resolver: ((value: boolean) => void) | null = null

/** Promise-based confirmation dialog: `if (await confirm({...})) { ... }` */
export function confirm(options: { title: string; description?: string; confirmLabel?: string; cancelLabel?: string; danger?: boolean; reasonLabel?: string }) {
  resolver?.(false)
  Object.assign(confirmState, { confirmLabel: 'Confirmar', cancelLabel: 'Voltar', danger: false, description: undefined, reasonLabel: '', reason: '' }, options, { open: true })
  return new Promise<boolean>((resolve) => (resolver = resolve))
}

/** Confirmação com campo de motivo opcional: devolve o texto (pode ser vazio) ou null quando a pessoa desiste. */
export async function confirmWithReason(options: { title: string; description?: string; confirmLabel?: string; reasonLabel?: string; danger?: boolean }) {
  const ok = await confirm({ reasonLabel: 'Motivo (opcional)', ...options })
  return ok ? confirmState.reason.trim() : null
}

export function resolveConfirm(value: boolean) {
  confirmState.open = false
  resolver?.(value)
  resolver = null
}
