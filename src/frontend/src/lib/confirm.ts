import { reactive } from 'vue'

export const confirmState = reactive({
  open: false,
  title: '',
  description: '' as string | undefined,
  confirmLabel: 'Confirmar',
  cancelLabel: 'Voltar',
  danger: false,
})

let resolver: ((value: boolean) => void) | null = null

/** Promise-based confirmation dialog: `if (await confirm({...})) { ... }` */
export function confirm(options: { title: string; description?: string; confirmLabel?: string; cancelLabel?: string; danger?: boolean }) {
  resolver?.(false)
  Object.assign(confirmState, { confirmLabel: 'Confirmar', cancelLabel: 'Voltar', danger: false, description: undefined }, options, { open: true })
  return new Promise<boolean>((resolve) => (resolver = resolve))
}

export function resolveConfirm(value: boolean) {
  confirmState.open = false
  resolver?.(value)
  resolver = null
}
