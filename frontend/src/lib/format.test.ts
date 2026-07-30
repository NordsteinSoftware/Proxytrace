import { describe, it, expect } from 'vitest'
import { fmtLatency, fmtTokens, fmtDuration, fmtElapsed, fmtPct, fmtPct100, fmtCost, fmtDate, fmtDateTime, fmtDateTimeShort, cachedPct } from './format'

describe('fmtLatency', () => {
  it('formats sub-second as ms', () => expect(fmtLatency(250)).toBe('250ms'))
  it('formats over 1s', () => expect(fmtLatency(1500)).toBe('1.5s'))
})

describe('fmtTokens', () => {
  it('passes through small numbers', () => expect(fmtTokens(500)).toBe('500'))
  it('formats thousands with k', () => expect(fmtTokens(1500)).toBe('1.5k'))
  it('formats millions with M', () => expect(fmtTokens(1_500_000)).toBe('1.5M'))
})

describe('cachedPct', () => {
  it('returns null when no cached tokens', () => expect(cachedPct(0, 1000)).toBeNull())
  it('returns null when no input tokens', () => expect(cachedPct(0, 0)).toBeNull())
  it('computes whole-percent share', () => expect(cachedPct(800, 1000)).toBe(80))
  it('rounds to nearest percent', () => expect(cachedPct(666, 1000)).toBe(67))
  it('clamps cached over input to 100%', () => expect(cachedPct(1500, 1000)).toBe(100))
  it('returns null when share rounds to zero', () => expect(cachedPct(1, 100000)).toBeNull())
})

describe('fmtElapsed', () => {
  it('formats m:ss', () => {
    expect(fmtElapsed(0)).toBe('0:00')
    expect(fmtElapsed(7)).toBe('0:07')
    expect(fmtElapsed(83)).toBe('1:23')
    expect(fmtElapsed(605)).toBe('10:05')
  })
  it('clamps negatives and fractions', () => {
    expect(fmtElapsed(-3)).toBe('0:00')
    expect(fmtElapsed(61.9)).toBe('1:01')
  })
})

describe('fmtDuration', () => {
  it('returns em dash for null', () => expect(fmtDuration(null)).toBe('—'))
  it('formats ms', () => expect(fmtDuration(500)).toBe('500ms'))
  it('formats seconds', () => expect(fmtDuration(1500)).toBe('1.5s'))
  it('formats minutes', () => expect(fmtDuration(90_000)).toBe('1m 30s'))
})

describe('fmtPct', () => {
  it('converts fraction to percent', () => expect(fmtPct(0.75)).toBe('75%'))
})

describe('fmtPct100', () => {
  it('formats an already-percent value', () => expect(fmtPct100(25)).toBe('25%'))
  it('rounds to whole percent', () => expect(fmtPct100(66.6)).toBe('67%'))
})

describe('fmtCost', () => {
  it('returns em dash for null', () => expect(fmtCost(null)).toBe('—'))
  it('formats exact zero with two decimals', () => expect(fmtCost(0)).toBe('€0.00'))
  it('formats a sub-cent amount as less-than, never €0.00', () => expect(fmtCost(0.0005)).toBe('<€0.01'))
  it('rounds a half-cent up rather than calling it sub-cent', () => expect(fmtCost(0.005)).toBe('€0.01'))
  it('keeps two decimals below one euro', () => expect(fmtCost(0.0123)).toBe('€0.01'))
  it('keeps two decimals above one euro', () => expect(fmtCost(1.3841)).toBe('€1.38'))
  it('rounds up just below one euro', () => expect(fmtCost(0.9999)).toBe('€1.00'))
  it('groups thousands', () => expect(fmtCost(12345.678)).toBe('€12,345.68'))
})

// Constructed in local time and read back in local time, so assertions are TZ-independent.
describe('date formatting (browser-local, dd.MM.yyyy, 24h)', () => {
  const iso = new Date(2026, 5, 8, 9, 5, 3).toISOString() // 8 Jun 2026 09:05:03 local

  it('fmtDate is dd.MM.yyyy', () => expect(fmtDate(iso)).toBe('08.06.2026'))
  it('fmtDateTimeShort adds 24h HH:mm', () => expect(fmtDateTimeShort(iso)).toBe('08.06.2026 09:05'))
  it('fmtDateTime adds seconds', () => expect(fmtDateTime(iso)).toBe('08.06.2026 09:05:03'))
  it('returns em dash for an invalid date', () => expect(fmtDate('not-a-date')).toBe('—'))
})
