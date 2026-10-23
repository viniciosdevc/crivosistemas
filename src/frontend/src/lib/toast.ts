import { reactive } from 'vue'

export type ToastTone = 'success' | 'error' | 'info'

export interface Toast {
  id: number
  tone: ToastTone
  title: string
  description?: string
  action?: { label: string; onClick: () => void }
}

export interface ToastOptions {
  action?: Toast['action']
  /** Milliseconds before it disappears. */
  duration?: number
}

let seq = 0
export const toasts = reactive<Toast[]>([])

function push(tone: ToastTone, title: string, description?: string, options?: ToastOptions) {
  const id = ++seq
  toasts.push({ id, tone, title, description, action: options?.action })
  setTimeout(() => dismiss(id), options?.duration ?? (tone === 'error' ? 6000 : 3500))
}

export function dismiss(id: number) {
  const i = toasts.findIndex((t) => t.id === id)
  if (i >= 0) toasts.splice(i, 1)
}

export const toast = {
  success: (title: string, description?: string) => push('success', title, description),
  error: (title: string, description?: string) => push('error', title, description),
  info: (title: string, description?: string, options?: ToastOptions) => push('info', title, description, options),
}
