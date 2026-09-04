import { Badge } from '@/components/ui/badge'
import type { SaleState } from '@/lib/catalog'

/**
 * The sale's state as a coloured chip, shown on both browse screens. A live sale
 * gets the loud default; an upcoming one the quieter secondary; an ended one the
 * outline that reads as "done". The label is the state's own word, so the chip
 * and any nearby countdown always agree.
 */
const VARIANT = {
  Live: 'default',
  Upcoming: 'secondary',
  Ended: 'outline',
} as const satisfies Record<SaleState, string>

const LABEL = {
  Live: 'On sale now',
  Upcoming: 'Upcoming',
  Ended: 'Sale ended',
} as const satisfies Record<SaleState, string>

export function SaleStateBadge({ state }: { state: SaleState }) {
  return <Badge variant={VARIANT[state]}>{LABEL[state]}</Badge>
}
