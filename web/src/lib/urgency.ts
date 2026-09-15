import type { SaleState } from '@/lib/catalog'

/**
 * How loudly a countdown should read. The single vocabulary behind every
 * countdown treatment in the app, so the Hold timer and a sale window that are
 * equally close to running out look equally urgent — and so "urgent" is decided
 * once, in a pure function a test can pin, rather than inside a component's
 * class names.
 *
 * `elapsed` is its own level rather than the loudest one: a countdown that has
 * already run out has nothing left to warn about, and treating it as critical
 * would leave a dead timer pulsing.
 */
export type Urgency = 'calm' | 'soon' | 'critical' | 'elapsed'

/**
 * The two countdowns in this app are two orders of magnitude apart — a Hold's
 * TTL is about two minutes (spec), a sale window hours or days — so they cannot
 * share thresholds: a minute left is almost the whole Hold but a rounding error
 * on a sale. Each scale states its own, in the units a buyer feels them in.
 */
const HOLD_THRESHOLDS = { soon: 60_000, critical: 20_000 } as const
const SALE_THRESHOLDS = { soon: 60 * 60_000, critical: 5 * 60_000 } as const

interface Thresholds {
  /** At or under this many ms left, the countdown reads `soon`. */
  soon: number
  /** At or under this many ms left, it reads `critical`. */
  critical: number
}

function levelFor(remainingMs: number, thresholds: Thresholds): Urgency {
  if (remainingMs <= 0) {
    return 'elapsed'
  }

  if (remainingMs <= thresholds.critical) {
    return 'critical'
  }

  return remainingMs <= thresholds.soon ? 'soon' : 'calm'
}

/**
 * How urgent a Hold's remaining TTL is. Tuned to the ~2 minute TTL: the last
 * minute is `soon` and the last twenty seconds `critical`, which is roughly the
 * point past which a buyer who has not started paying will not finish in time.
 */
export function holdUrgency(remainingMs: number): Urgency {
  return levelFor(remainingMs, HOLD_THRESHOLDS)
}

/**
 * How urgent a flight's sale window is, from the same inputs the countdown text
 * is built from.
 *
 * Only a Live window can be urgent. An Upcoming sale is anticipation rather than
 * pressure — nothing is being lost while a buyer waits for it to open, and a
 * flight they cannot buy yet has no call to shout — so it stays `calm` however
 * close it is. An Ended one is `elapsed`, as is a Live one whose end has already
 * passed: the state is Catalog's, computed when it answered, and the clock here
 * has moved on since.
 */
export function saleUrgency(state: SaleState, saleEndsAt: string, now: Date): Urgency {
  if (state === 'Ended') {
    return 'elapsed'
  }

  if (state === 'Upcoming') {
    return 'calm'
  }

  return levelFor(new Date(saleEndsAt).getTime() - now.getTime(), SALE_THRESHOLDS)
}
