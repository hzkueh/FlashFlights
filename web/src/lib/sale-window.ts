import type { Flight, SaleState } from '@/lib/catalog'

/** The window itself — all `saleStateAt` needs, so a test can pass two strings. */
export type SaleWindow = Pick<Flight, 'saleStartsAt' | 'saleEndsAt'>

/**
 * A flight's sale state as of `now`, recomputed here rather than read off the
 * server's answer.
 *
 * `Flight.saleState` is a snapshot: Catalog computes it from the window when it
 * responds, so it is only true for the instant of that response. A browse page
 * is open far longer than that — long enough for an Upcoming sale to open while
 * the reader is looking at it — and nothing re-fetches on its own, so the
 * server's word goes stale on screen while the countdown beside it keeps
 * ticking. The state is a pure function of the window, which the same response
 * already carries, so the client can keep it current without asking again.
 *
 * The boundaries are Catalog's, deliberately: `FlightCatalogService.StateOf`
 * treats the window as inclusive at both ends — at the instant it opens the sale
 * is Live, and at the instant it closes it is Ended. Recomputing here is only
 * safe while these two agree, so a change to one belongs in the other.
 *
 * The client's clock is its own, not the server's, so a badly-set machine reads
 * the flip early or late. That is the same clock the countdown has always
 * trusted, and being seconds out on a window measured in hours costs a buyer
 * nothing: Ordering decides what may actually be held, and it uses its own.
 */
export function saleStateAt(window: SaleWindow, now: Date): SaleState {
  const at = now.getTime()

  if (at < new Date(window.saleStartsAt).getTime()) {
    return 'Upcoming'
  }

  return at < new Date(window.saleEndsAt).getTime() ? 'Live' : 'Ended'
}
