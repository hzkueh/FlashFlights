import { motion } from 'motion/react'

import { useNow } from '@/hooks/use-now'
import { useReducedMotion } from '@/hooks/use-reduced-motion'
import type { Flight } from '@/lib/catalog'
import { saleTimeLeft } from '@/lib/format'
import { type Urgency, saleUrgency } from '@/lib/urgency'
import { cn } from '@/lib/utils'

/**
 * How each urgency reads. Colour carries the level on its own — a reader who has
 * asked for reduced motion, or who cannot perceive the pulse, still sees a
 * countdown change from muted to amber to red as its time runs out.
 */
const URGENCY_STYLE = {
  calm: 'text-muted-foreground',
  soon: 'text-urgent font-medium',
  critical: 'text-destructive font-semibold',
  elapsed: 'text-muted-foreground',
} as const satisfies Record<Urgency, string>

/** Only the last level pulses; anything sooner would leave the page always moving. */
const PULSING: Urgency = 'critical'

interface CountdownProps {
  urgency: Urgency
  /** `timer` for a countdown a buyer is acting against; the default is plain text. */
  role?: 'timer' | 'status'
  className?: string
  children: React.ReactNode
}

/**
 * The one countdown treatment in the app: tabular figures that do not reflow as
 * they tick, a colour for how much time is left, and — in the final stretch — a
 * slow pulse.
 *
 * The urgency is decided by `@/lib/urgency` and passed in, so the two countdowns
 * this wraps (a Hold's TTL and a sale window, two orders of magnitude apart)
 * share one treatment without sharing thresholds. It is also declared on the
 * element as `data-urgency`, which is both what the tests assert and how the
 * treatment stays checkable in the browser.
 */
export function Countdown({ urgency, role, className, children }: CountdownProps) {
  const reducedMotion = useReducedMotion()
  const pulsing = urgency === PULSING && !reducedMotion

  return (
    <motion.p
      role={role}
      // A countdown that has turned critical is worth announcing once; a calm one
      // ticking every second would talk over everything else on the page.
      aria-live={role === 'timer' && urgency === 'critical' ? 'assertive' : 'polite'}
      data-urgency={urgency}
      data-pulse={pulsing ? 'on' : 'off'}
      animate={pulsing ? { opacity: [1, 0.5, 1] } : { opacity: 1 }}
      transition={
        pulsing ? { duration: 1.1, repeat: Infinity, ease: 'easeInOut' } : { duration: 0.2 }
      }
      className={cn('text-sm tabular-nums transition-colors', URGENCY_STYLE[urgency], className)}
    >
      {children}
    </motion.p>
  )
}

/**
 * A flight's sale window, counting down: to its opening while upcoming, to its
 * close while live, and stating plainly that it is over once ended. Owns its own
 * clock, so a page renders one of these per flight rather than threading `now`
 * down to every card.
 */
export function SaleCountdown({ flight, className }: { flight: Flight; className?: string }) {
  const now = useNow()

  return (
    <Countdown
      urgency={saleUrgency(flight.saleState, flight.saleEndsAt, now)}
      className={className}
    >
      {saleTimeLeft(flight.saleState, flight.saleStartsAt, flight.saleEndsAt, now)}
    </Countdown>
  )
}
