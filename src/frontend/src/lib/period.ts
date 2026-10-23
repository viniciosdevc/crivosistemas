import { todayIso } from './format'

export type PeriodPreset = 'today' | '7d' | '30d' | 'month' | 'custom'

export const periodOptions: { value: PeriodPreset; label: string }[] = [
  { value: 'today', label: 'Hoje' },
  { value: '7d', label: '7 dias' },
  { value: '30d', label: '30 dias' },
  { value: 'month', label: 'Mês atual' },
  { value: 'custom', label: 'Personalizado' },
]

const ISO = /^\d{4}-\d{2}-\d{2}$/

export function isIsoDay(value: string | null | undefined): value is string {
  return !!value && ISO.test(value) && !Number.isNaN(Date.parse(`${value}T12:00:00Z`))
}

/** Soma dias a um dia de calendário (AAAA-MM-DD) sem passar por fuso. */
export function addDays(day: string, delta: number) {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(Date.UTC(y!, m! - 1, d! + delta, 12)).toISOString().slice(0, 10)
}

/** Dias entre duas datas, contando os dois extremos. */
export function daysBetween(de: string, ate: string) {
  return Math.round((Date.parse(`${ate}T12:00:00Z`) - Date.parse(`${de}T12:00:00Z`)) / 86_400_000) + 1
}

/** Intervalo [de, ate] (dias em America/Sao_Paulo) de um período pré-definido. `custom` usa os dias informados, trocando se vierem invertidos. */
export function resolvePeriod(preset: PeriodPreset, today: string = todayIso(), custom?: { de?: string | null; ate?: string | null }) {
  switch (preset) {
    case 'today':
      return { de: today, ate: today }
    case '7d':
      return { de: addDays(today, -6), ate: today }
    case 'month':
      return { de: `${today.slice(0, 8)}01`, ate: today }
    case 'custom': {
      const ate = isIsoDay(custom?.ate) ? custom.ate : today
      const de = isIsoDay(custom?.de) ? custom.de : addDays(ate, -29)
      return de <= ate ? { de, ate } : { de: ate, ate: de }
    }
    default:
      return { de: addDays(today, -29), ate: today }
  }
}

export function isPeriodPreset(value: string): value is PeriodPreset {
  return periodOptions.some((o) => o.value === value)
}
