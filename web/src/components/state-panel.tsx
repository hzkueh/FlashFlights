import { cn } from '@/lib/utils'

/**
 * The bordered card a page shows when it has nothing else to show: a failure, an
 * empty list, a prompt to sign in. One shape for all of them, so an error on the
 * bookings page and an error on the seat map read as the same kind of event
 * rather than as two unrelated designs.
 *
 * A failure is always an `alert`: it interrupts what the reader was trying to do.
 * A neutral panel is silent by default, because an empty list or a sign-in prompt
 * is ordinary page content rather than something that just happened — a caller
 * whose panel *did* just happen (a Hold lapsing under the buyer) asks for
 * `role="status"` itself. Actions (a link back, a retry) are the caller's, passed
 * as children.
 */
export function StatePanel({
  tone = 'neutral',
  role,
  title,
  children,
  className,
}: {
  tone?: 'neutral' | 'error'
  /** Announce a neutral panel that appeared in response to something changing. Ignored when `tone` is `error`. */
  role?: 'status'
  /** The one line that says what happened. Optional — a bare sentence in `children` is fine. */
  title?: string
  children?: React.ReactNode
  className?: string
}) {
  const error = tone === 'error'

  return (
    <div
      role={error ? 'alert' : role}
      className={cn(
        'space-y-3 rounded-lg border p-5',
        error ? 'border-destructive/40 bg-destructive/5' : 'bg-card',
        className,
      )}
    >
      {title !== undefined && (
        <p className={cn('text-sm font-medium', error && 'text-destructive')}>{title}</p>
      )}
      {children}
    </div>
  )
}
