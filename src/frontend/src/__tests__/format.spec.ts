import { describe, expect, it } from 'vitest'
import { formatMoney, formatPhone, initials, maskDocument, maskPhone, moneyParts, pluralize } from '@/lib/format'

describe('format', () => {
  it('formats BRL with regular spaces', () => {
    expect(formatMoney(1234.5)).toBe('R$ 1.234,50')
    expect(formatMoney(null)).toBe('R$ 0,00')
  })

  it('splits money for the large display', () => {
    expect(moneyParts(1234.56)).toEqual({ int: 'R$ 1.234', cents: '56' })
  })

  it('formats and masks phones', () => {
    expect(formatPhone('11987654321')).toBe('(11) 98765-4321')
    expect(formatPhone('1133334444')).toBe('(11) 3333-4444')
    expect(maskPhone('119')).toBe('(11) 9')
    expect(maskPhone('11987654321999')).toBe('(11) 98765-4321')
  })

  it('masks CPF and CNPJ', () => {
    expect(maskDocument('12345678901')).toBe('123.456.789-01')
    expect(maskDocument('12345678000199')).toBe('12.345.678/0001-99')
  })

  it('builds initials and plurals', () => {
    expect(initials('Maria da Silva')).toBe('MS')
    expect(initials('Ana')).toBe('A')
    expect(pluralize(1, 'item', 'itens')).toBe('1 item')
    expect(pluralize(3, 'item', 'itens')).toBe('3 itens')
  })
})
