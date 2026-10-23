import { describe, expect, it } from 'vitest'
import { formatDateTime, formatDay, formatDecimal, formatInt, formatMoney, formatPercent, formatPhone, formatUsd, initials, isoDay, maskDocument, maskPhone, moneyParts, parseDecimal, pluralize, shortWallet, walletExplorerUrl } from '@/lib/format'

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

describe('números e datas da gestão', () => {
  it('lê decimais no padrão brasileiro e com ponto', () => {
    expect(parseDecimal('12,5')).toBe(12.5)
    expect(parseDecimal('1.234,56')).toBe(1234.56)
    expect(parseDecimal('12.5')).toBe(12.5)
    expect(parseDecimal(' 7 ')).toBe(7)
    expect(parseDecimal('')).toBeNull()
    expect(parseDecimal('abc')).toBeNull()
    expect(parseDecimal('1,2,3')).toBeNull()
    expect(parseDecimal(4)).toBe(4)
  })

  it('formata percentual, dólar e inteiros', () => {
    expect(formatPercent(12.34)).toBe('12,3%')
    expect(formatUsd(1234.5)).toBe('US$ 1,234.50')
    expect(formatInt(12345)).toBe('12.345')
    expect(formatDecimal(5.7)).toBe('5,7')
  })

  it('mostra datas no fuso de São Paulo, sem deslocar o dia', () => {
    // 01:30 UTC ainda é o dia anterior em São Paulo (UTC-3).
    expect(formatDateTime('2026-10-22T01:30:00Z', 'America/Sao_Paulo')).toContain('21 out')
    expect(formatDateTime('2026-10-22T01:30:00Z', 'America/Sao_Paulo')).toContain('22:30')
    expect(formatDay('2026-10-21')).toContain('21 out')
    expect(isoDay('2026-10-22T01:30:00Z', 'America/Sao_Paulo')).toBe('2026-10-21')
  })
})

describe('carteira', () => {
  const end = '0xabcdef0123456789abcdef0123456789abcdef01'
  it('abrevia o endereço e monta o link do explorer', () => {
    expect(shortWallet(end)).toBe('0xabcd…ef01')
    expect(shortWallet(null)).toBe('')
    expect(walletExplorerUrl(end)).toBe(`https://explorer.immutable.com/address/${end}`)
  })
})
