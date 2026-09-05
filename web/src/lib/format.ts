import type { SaleState } from '@/lib/catalog'

/**
 * Presentation helpers for the browse screens. The time-relative ones take
 * `now` explicitly rather than reading the clock themselves, so they stay pure
 * and a test can pin the moment it asserts against.
 */

// A single placeholder currency for the whole catalog — the domain model carries
// no per-flight currency (out of scope), so this is the one knob to turn when it
// eventually does.
const CURRENCY = 'GBP'

const priceFormat = new Intl.NumberFormat('en-GB', { style: 'currency', currency: CURRENCY })

const departureFormat = new Intl.DateTimeFormat('en-GB', {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
})

export function formatPrice(amount: number): string {
  return priceFormat.format(amount)
}

export function formatDeparture(iso: string): string {
  return departureFormat.format(new Date(iso))
}

/**
 * A date and time a buyer reads back — a Booking's confirmed moment, say. The
 * same rendering as a departure, named for the general case so its call sites
 * don't read as "departure"; delegates rather than repeat the format.
 */
export function formatDateTime(iso: string): string {
  return formatDeparture(iso)
}

/** "LHR → BCN" — the route as a buyer scans it. */
export function formatRoute(origin: string, destination: string): string {
  return `${origin} → ${destination}`
}

/**
 * The one line of urgency the list and detail pages both show: how long an
 * upcoming sale is until it opens, how long a live one is until it closes, and
 * a plain statement once it has ended. Clamped at zero, so a countdown that has
 * just run out reads "0s" rather than a negative.
 */
export function saleTimeLeft(
  state: SaleState,
  saleStartsAt: string,
  saleEndsAt: string,
  now: Date,
): string {
  if (state === 'Ended') {
    return 'Sale ended'
  }

  if (state === 'Upcoming') {
    return `Opens in ${humanizeDuration(new Date(saleStartsAt).getTime() - now.getTime())}`
  }

  return `Ends in ${humanizeDuration(new Date(saleEndsAt).getTime() - now.getTime())}`
}

/** A coarse, human duration: days+hours, then hours+minutes, then minutes+seconds. */
export function humanizeDuration(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000))
  const days = Math.floor(totalSeconds / 86_400)
  const hours = Math.floor((totalSeconds % 86_400) / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const seconds = totalSeconds % 60

  if (days > 0) {
    return `${days}d ${hours}h`
  }

  if (hours > 0) {
    return `${hours}h ${minutes}m`
  }

  return `${minutes}m ${seconds}s`
}
