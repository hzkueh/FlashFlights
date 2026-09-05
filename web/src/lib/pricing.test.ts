import { describe, expect, it } from 'vitest'

import { savingsPercent } from '@/lib/pricing'

/**
 * The single place that decides whether — and how large — a saving is shown.
 * ReferenceFare is optional and display-only (CONTEXT.md): a saving exists only
 * when the reference is strictly above the flash price, and anything else
 * degrades to "no saving shown" (null) rather than a zero or negative badge.
 */
describe('savingsPercent', () => {
  it('is the whole-number percentage off the reference fare', () => {
    // (229 - 149) / 229 = 34.9% -> 35
    expect(savingsPercent({ flashPrice: 149, referenceFare: 229 })).toBe(35)
  })

  it('rounds to the nearest whole percent', () => {
    // (100 - 75) / 100 = exactly 25%
    expect(savingsPercent({ flashPrice: 75, referenceFare: 100 })).toBe(25)
  })

  it('is null when there is no reference fare', () => {
    expect(savingsPercent({ flashPrice: 149, referenceFare: null })).toBeNull()
  })

  it('is null when the reference fare equals the flash price', () => {
    expect(savingsPercent({ flashPrice: 149, referenceFare: 149 })).toBeNull()
  })

  it('is null when the reference fare is below the flash price', () => {
    // Nonsensical data must not surface a negative "saving".
    expect(savingsPercent({ flashPrice: 149, referenceFare: 99 })).toBeNull()
  })
})
