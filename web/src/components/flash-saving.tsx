import { Badge } from '@/components/ui/badge'
import type { Flight } from '@/lib/catalog'
import { formatPrice } from '@/lib/format'
import { savingsPercent } from '@/lib/pricing'

/**
 * The saving off a flight's ReferenceFare, shown beside its FlashPrice on both
 * browse screens: the standing fare struck through, then a "Save X%" chip. The
 * flash price itself stays the caller's concern — this renders only the
 * "marked down from" half, and renders nothing at all when there is no saving
 * to show (no ReferenceFare, or one that is not above the FlashPrice), so a
 * flight without a discount is simply a bare price. Whether a saving exists is
 * decided once, in `savingsPercent` (CONTEXT.md: the saving is derived, never
 * stored).
 */
export function FlashSaving({ flight }: { flight: Flight }) {
  const percent = savingsPercent(flight)

  if (percent === null || flight.referenceFare === null) {
    return null
  }

  return (
    <span className="flex items-baseline gap-2">
      <span className="text-muted-foreground text-sm line-through tabular-nums">
        {formatPrice(flight.referenceFare)}
      </span>
      <Badge variant="destructive">Save {percent}%</Badge>
    </span>
  )
}
