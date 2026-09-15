import { describe, expect, it } from 'vitest'

import { saleStateAt } from '@/lib/sale-window'

/**
 * The client's copy of Catalog's rule (`FlightCatalogService.StateOf`). The
 * boundary cases are the point: they are what the server does, and the two only
 * stay interchangeable while these pass.
 */
const WINDOW = { saleStartsAt: '2026-09-15T10:00:00Z', saleEndsAt: '2026-09-15T18:00:00Z' }

describe('saleStateAt', () => {
  it('is upcoming before the window opens', () => {
    expect(saleStateAt(WINDOW, new Date('2026-09-15T09:59:59Z'))).toBe('Upcoming')
  })

  it('is live at the instant the window opens, not a tick later', () => {
    expect(saleStateAt(WINDOW, new Date('2026-09-15T10:00:00Z'))).toBe('Live')
  })

  it('is live inside the window', () => {
    expect(saleStateAt(WINDOW, new Date('2026-09-15T14:00:00Z'))).toBe('Live')
  })

  it('is ended at the instant the window closes', () => {
    expect(saleStateAt(WINDOW, new Date('2026-09-15T18:00:00Z'))).toBe('Ended')
  })

  it('is ended after the window closes', () => {
    expect(saleStateAt(WINDOW, new Date('2026-09-15T18:00:01Z'))).toBe('Ended')
  })
})
