/** Time-of-day helpers for the time picker. The wire/model format is 'HH:MM'; the API may send 'HH:MM:SS'. */

const pad = (n: number) => String(n).padStart(2, '0')

/** Builds 'HH:MM' from numbers, or null when out of range. */
export function makeTime(h: number, m: number): string | null {
  if (!Number.isInteger(h) || !Number.isInteger(m) || h < 0 || h > 23 || m < 0 || m > 59) return null
  return `${pad(h)}:${pad(m)}`
}

/**
 * Lenient parser for typed input: '9', '930', '0930', '9:30', '09:30', '9h30', '09:30:15'.
 * Returns a normalized 'HH:MM' or null when it is not a valid time of day.
 */
export function parseTime(raw: string | null | undefined): string | null {
  const s = (raw ?? '').trim().toLowerCase()
  if (!s) return null
  if (/^\d+$/.test(s)) {
    if (s.length <= 2) return makeTime(Number(s), 0)
    if (s.length === 3) return makeTime(Number(s.slice(0, 1)), Number(s.slice(1)))
    if (s.length === 4) return makeTime(Number(s.slice(0, 2)), Number(s.slice(2)))
    return null
  }
  const m = /^(\d{1,2})\s*[:h.]\s*(\d{1,2})?(?:\s*[:.]\s*\d{1,2})?$/.exec(s)
  if (!m) return null
  return makeTime(Number(m[1]), m[2] === undefined ? 0 : Number(m[2]))
}

/** Normalizes an API value ('HH:MM' or 'HH:MM:SS') to 'HH:MM'; '' when empty or invalid. */
export function normalizeTime(v: string | null | undefined): string {
  if (!v) return ''
  return parseTime(v.length > 5 ? v.slice(0, 5) : v) ?? ''
}

/** 'HH:MM' to 'HH:MM:SS' for APIs that expect seconds. */
export function toApiTime(v: string): string {
  return v.length === 5 ? `${v}:00` : v
}

/** Masks while typing: keeps digits and one colon, inserts it after the hour. */
export function maskTime(raw: string): string {
  const kept = raw.replace(/[^\d:]/g, '')
  if (kept.includes(':')) {
    const [h = '', ...rest] = kept.split(':')
    return `${h.slice(0, 2)}:${rest.join('').slice(0, 2)}`
  }
  if (kept.length >= 4) return `${kept.slice(0, 2)}:${kept.slice(2, 4)}`
  if (kept.length === 3 && Number(kept.slice(0, 2)) <= 23) return `${kept.slice(0, 2)}:${kept.slice(2)}`
  return kept
}

/** Minutes offered in the picker: every `step`, plus the current one when it is off the grid. */
export function minuteOptions(step: number, current?: number): number[] {
  const s = Math.min(Math.max(Math.floor(step) || 5, 1), 30)
  const list: number[] = []
  for (let m = 0; m < 60; m += s) list.push(m)
  if (current !== undefined && !list.includes(current)) list.push(current)
  return list.sort((a, b) => a - b)
}

/** Whether 'HH:MM' fits between optional min and max (same-day strings compare lexically). */
export function inTimeRange(v: string, min?: string, max?: string): boolean {
  return (!min || v >= min) && (!max || v <= max)
}
