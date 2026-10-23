import { describe, expect, it } from 'vitest'
import { addDays, daysBetween, isIsoDay, resolvePeriod } from '@/lib/period'

describe('período', () => {
  const today = '2026-10-23'

  it('resolve os períodos pré-definidos em dias de São Paulo', () => {
    expect(resolvePeriod('today', today)).toEqual({ de: '2026-10-23', ate: '2026-10-23' })
    expect(resolvePeriod('7d', today)).toEqual({ de: '2026-10-17', ate: '2026-10-23' })
    expect(resolvePeriod('30d', today)).toEqual({ de: '2026-09-24', ate: '2026-10-23' })
    expect(resolvePeriod('month', today)).toEqual({ de: '2026-10-01', ate: '2026-10-23' })
  })

  it('atravessa virada de mês e de ano', () => {
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28')
    expect(addDays('2026-01-01', -1)).toBe('2025-12-31')
    expect(resolvePeriod('7d', '2026-01-03')).toEqual({ de: '2025-12-28', ate: '2026-01-03' })
  })

  it('personalizado usa os dias informados, troca se invertidos e completa o que faltar', () => {
    expect(resolvePeriod('custom', today, { de: '2026-10-01', ate: '2026-10-10' })).toEqual({ de: '2026-10-01', ate: '2026-10-10' })
    expect(resolvePeriod('custom', today, { de: '2026-10-10', ate: '2026-10-01' })).toEqual({ de: '2026-10-01', ate: '2026-10-10' })
    expect(resolvePeriod('custom', today, { de: 'lixo', ate: '' })).toEqual({ de: '2026-09-24', ate: '2026-10-23' })
  })

  it('conta dias e valida datas', () => {
    expect(daysBetween('2026-10-01', '2026-10-01')).toBe(1)
    expect(daysBetween('2026-10-01', '2026-10-31')).toBe(31)
    expect(isIsoDay('2026-10-23')).toBe(true)
    expect(isIsoDay('23/10/2026')).toBe(false)
    expect(isIsoDay('2026-13-40')).toBe(false)
  })
})
