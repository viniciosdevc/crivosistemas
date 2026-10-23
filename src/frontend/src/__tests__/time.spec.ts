import { describe, expect, it } from 'vitest'
import { inTimeRange, maskTime, minuteOptions, normalizeTime, parseTime, toApiTime } from '@/lib/time'

describe('time', () => {
  it('parses typed shortcuts', () => {
    expect(parseTime('9')).toBe('09:00')
    expect(parseTime('930')).toBe('09:30')
    expect(parseTime('0930')).toBe('09:30')
    expect(parseTime('9:30')).toBe('09:30')
    expect(parseTime('09:05')).toBe('09:05')
    expect(parseTime('9h30')).toBe('09:30')
    expect(parseTime('18:00:00')).toBe('18:00')
  })
  it('rejects invalid times', () => {
    for (const v of ['', 'abc', '24:00', '12:60', '99', '12345', '9:'.repeat(3)]) expect(parseTime(v)).toBeNull()
  })
  it('normalizes API values', () => {
    expect(normalizeTime('08:00:00')).toBe('08:00')
    expect(normalizeTime('08:00')).toBe('08:00')
    expect(normalizeTime(null)).toBe('')
    expect(toApiTime('08:00')).toBe('08:00:00')
    expect(toApiTime('08:00:00')).toBe('08:00:00')
  })
  it('masks while typing', () => {
    expect(maskTime('0')).toBe('0')
    expect(maskTime('093')).toBe('09:3')
    expect(maskTime('0930')).toBe('09:30')
    expect(maskTime('9:3x')).toBe('9:3')
    expect(maskTime('93')).toBe('93')
  })
  it('builds minute options and checks ranges', () => {
    expect(minuteOptions(15)).toEqual([0, 15, 30, 45])
    expect(minuteOptions(15, 7)).toEqual([0, 7, 15, 30, 45])
    expect(inTimeRange('10:00', '09:00', '18:00')).toBe(true)
    expect(inTimeRange('08:00', '09:00')).toBe(false)
  })
})
