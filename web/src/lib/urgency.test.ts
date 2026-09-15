import { describe, expect, it } from 'vitest'

import { holdUrgency, saleUrgency } from '@/lib/urgency'

/**
 * The one place that decides how loudly a countdown should read. Two scales,
 * because the two countdowns in this app are two orders of magnitude apart: a
 * Hold's TTL is about two minutes, a sale window hours or days. A single
 * threshold would leave every Hold permanently critical or every sale
 * permanently calm.
 */
describe('holdUrgency', () => {
  const seconds = (n: number) => n * 1000

  it('is calm while there is comfortably enough time to pay', () => {
    expect(holdUrgency(seconds(115))).toBe('calm')
  })

  it('turns urgent as the last minute starts', () => {
    expect(holdUrgency(seconds(60))).toBe('soon')
    expect(holdUrgency(seconds(45))).toBe('soon')
  })

  it('turns critical in the final seconds, where a buyer may still act', () => {
    expect(holdUrgency(seconds(20))).toBe('critical')
    expect(holdUrgency(seconds(1))).toBe('critical')
  })

  it('is elapsed at zero and beyond, so nothing pulses at a dead countdown', () => {
    expect(holdUrgency(0)).toBe('elapsed')
    expect(holdUrgency(seconds(-30))).toBe('elapsed')
  })
})

describe('saleUrgency', () => {
  const now = new Date('2026-09-15T12:00:00Z')
  const inMinutes = (n: number) => new Date(now.getTime() + n * 60_000).toISOString()

  it('is calm for a live sale with hours left on its window', () => {
    expect(saleUrgency('Live', inMinutes(180), now)).toBe('calm')
  })

  it('turns urgent within the last hour of a live window', () => {
    expect(saleUrgency('Live', inMinutes(40), now)).toBe('soon')
  })

  it('turns critical in the last few minutes of a live window', () => {
    expect(saleUrgency('Live', inMinutes(3), now)).toBe('critical')
  })

  /**
   * An upcoming sale is anticipation, not pressure — nothing is being lost while
   * a buyer waits for it to open, so its countdown never shouts.
   */
  it('stays calm for an upcoming sale however close it is to opening', () => {
    expect(saleUrgency('Upcoming', inMinutes(120), now)).toBe('calm')
  })

  it('is elapsed once the sale has ended', () => {
    expect(saleUrgency('Ended', inMinutes(-60), now)).toBe('elapsed')
  })

  it('is elapsed for a live window whose end has already passed', () => {
    // The state is Catalog's, computed when it answered; the clock has moved on.
    expect(saleUrgency('Live', inMinutes(-1), now)).toBe('elapsed')
  })
})
