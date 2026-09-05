import type { Flight } from '@/lib/catalog'

/**
 * The only thing this app derives from a Flight's ReferenceFare: how much a
 * buyer saves off the standing fare, as a whole-number percentage, or `null`
 * when there is no saving to show.
 *
 * ReferenceFare is display-only and optional (CONTEXT.md) — the saving is
 * computed here, never stored — so this is the single place that decides
 * whether the strike-through and badge appear. It returns `null` both when the
 * Flight carries no ReferenceFare and when the data is nonsensical (a reference
 * that is not strictly above the FlashPrice), so a bad row degrades to "no
 * saving shown" rather than a "SAVE 0%" or negative badge. Because the badge
 * only renders when the reference exceeds the flash price, the divisor is
 * always positive — no divide-by-zero.
 */
export function savingsPercent(flight: Pick<Flight, 'flashPrice' | 'referenceFare'>): number | null {
  const { flashPrice, referenceFare } = flight

  if (referenceFare === null || referenceFare <= flashPrice) {
    return null
  }

  return Math.round(((referenceFare - flashPrice) / referenceFare) * 100)
}
