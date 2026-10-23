const money = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
import { ref } from 'vue'

export const DEFAULT_TIME_ZONE = 'America/Sao_Paulo'

/** Fuso IANA da empresa: toda data/hora mostrada ao usuário usa este fuso, nunca o do navegador. */
const zone = ref(DEFAULT_TIME_ZONE)

export function setTimeZone(value: string | null | undefined) {
  zone.value = value && isValidZone(value) ? value : DEFAULT_TIME_ZONE
}

export function currentTimeZone() {
  return zone.value
}

function isValidZone(value: string) {
  try {
    new Intl.DateTimeFormat('pt-BR', { timeZone: value })
    return true
  } catch {
    return false
  }
}

const cache = new Map<string, Intl.DateTimeFormat>()
function fmt(kind: 'date' | 'short' | 'time' | 'day', timeZone: string) {
  const key = `${kind}|${timeZone}`
  let f = cache.get(key)
  if (!f) {
    const opts: Intl.DateTimeFormatOptions =
      kind === 'time' ? { hour: '2-digit', minute: '2-digit' } : kind === 'short' ? { day: '2-digit', month: 'short' } : { day: '2-digit', month: 'short', year: 'numeric' }
    f = new Intl.DateTimeFormat('pt-BR', { ...opts, timeZone })
    cache.set(key, f)
  }
  return f
}

export function formatMoney(value: number | null | undefined) {
  return money.format(value ?? 0).replace(/ /g, ' ')
}

/** Splits a money value for the large display style ("R$ 1.234" + ",56"). */
export function moneyParts(value: number) {
  const [int, cents] = formatMoney(value).split(',')
  return { int: int ?? '', cents: cents ?? '00' }
}

function toDate(value: string | Date) {
  return typeof value === 'string' ? new Date(value) : value
}

/** "20 out. 2026" */
export function formatDate(value: string | Date | null | undefined, timeZone: string = zone.value) {
  return value ? fmt('date', timeZone).format(toDate(value)).replace(' de ', ' ').replace(' de ', ' ') : '—'
}

export function formatDateShort(value: string | Date, timeZone: string = zone.value) {
  return fmt('short', timeZone).format(toDate(value)).replace(' de ', ' ')
}

export function formatTime(value: string | Date, timeZone: string = zone.value) {
  return fmt('time', timeZone).format(toDate(value))
}

/** "20 out. 2026 às 16:14" no fuso da empresa. */
export function formatDateTime(value: string | Date | null | undefined, timeZone: string = zone.value) {
  return value ? `${formatDate(value, timeZone)} às ${formatTime(value, timeZone)}` : '—'
}

/** DateOnly from the API ("2026-10-21") without timezone shifts. */
export function formatDay(value: string | null | undefined) {
  if (!value) return '—'
  const [y, m, d] = value.split('-').map(Number)
  // A calendar date has no zone: format it as UTC so it never shifts a day.
  return formatDate(new Date(Date.UTC(y!, m! - 1, d!, 12)), 'UTC')
}

/** Calendar date ("2026-10-21") of an instant no fuso da empresa. */
export function isoDay(value: string | Date, timeZone: string = zone.value) {
  const p = Object.fromEntries(
    new Intl.DateTimeFormat('en-US', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(toDate(value)).map((x) => [x.type, x.value]),
  )
  return `${p.year}-${p.month}-${p.day}`
}

/** "Today" no fuso da empresa, as an ISO date. */
export function todayIso(timeZone: string = zone.value) {
  return isoDay(new Date(), timeZone)
}

export function formatPhone(value: string | null | undefined) {
  if (!value) return ''
  const d = value.replace(/\D/g, '')
  if (d.length === 11) return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`
  if (d.length === 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`
  return value
}

/** Progressive mask while typing: (11) 99999-9999 */
export function maskPhone(value: string) {
  const d = value.replace(/\D/g, '').slice(0, 11)
  if (d.length <= 2) return d.length ? `(${d}` : ''
  if (d.length <= 6) return `(${d.slice(0, 2)}) ${d.slice(2)}`
  if (d.length <= 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`
  return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`
}

export function maskDocument(value: string) {
  const d = value.replace(/\D/g, '').slice(0, 14)
  if (d.length <= 11)
    return d.replace(/(\d{3})(\d)/, '$1.$2').replace(/(\d{3})(\d)/, '$1.$2').replace(/(\d{3})(\d{1,2})$/, '$1-$2')
  return d.replace(/^(\d{2})(\d)/, '$1.$2').replace(/^(\d{2})\.(\d{3})(\d)/, '$1.$2.$3').replace(/\.(\d{3})(\d)/, '.$1/$2').replace(/(\d{4})(\d)/, '$1-$2')
}

export function initials(name: string) {
  const parts = name.trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : '')).toUpperCase()
}

export function pluralize(count: number, one: string, many: string) {
  return `${count} ${count === 1 ? one : many}`
}

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
const decimal = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 4 })
const integer = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 0 })

/** "US$ 12.50" */
export function formatUsd(value: number | null | undefined) {
  return usd.format(value ?? 0).replace('$', 'US$ ')
}

/** "12,3456": número sem moeda, até 4 casas. */
export function formatDecimal(value: number | null | undefined) {
  return decimal.format(value ?? 0)
}

export function formatInt(value: number | null | undefined) {
  return integer.format(value ?? 0)
}

/** "12,5%" a partir de um valor já em percentual (12.5). */
export function formatPercent(value: number | null | undefined) {
  return `${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 }).format(value ?? 0)}%`
}

/** Lê um número digitado no padrão brasileiro ("1.234,56" ou "12,5") ou com ponto ("12.5"). null quando vazio ou inválido. */
export function parseDecimal(raw: string | number | null | undefined): number | null {
  if (typeof raw === 'number') return Number.isFinite(raw) ? raw : null
  const s = (raw ?? '').trim().replace(/\s/g, '')
  if (!s) return null
  // "1.234,56" -> ponto é milhar; "12.5" -> ponto é decimal.
  const normalized = s.includes(',') ? s.replace(/\./g, '').replace(',', '.') : s
  if (!/^-?\d*\.?\d+$/.test(normalized)) return null
  const n = Number(normalized)
  return Number.isFinite(n) ? n : null
}

/** Endereço de carteira abreviado: 0x1234…abcd. */
export function shortWallet(address: string | null | undefined) {
  const a = address?.trim()
  if (!a) return ''
  return a.length > 12 ? `${a.slice(0, 6)}…${a.slice(-4)}` : a
}

/** Página do endereço no explorer da Immutable zkEVM. */
export function walletExplorerUrl(address: string) {
  return `https://explorer.immutable.com/address/${encodeURIComponent(address.trim())}`
}
