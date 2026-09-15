import { useEffect, useState } from 'react'
import { Link } from 'react-router'

import { Button } from '@/components/ui/button'
import { useSession } from '@/hooks/use-session'
import type { Flight } from '@/lib/catalog'
import { listWatchedFlightIds, unwatchFlight, watchFlight } from '@/lib/notifications'

/**
 * Watch and un-watch a flight whose sale hasn't opened yet.
 *
 * <p>Shown only on an upcoming sale, because that is the only thing a Watch can
 * do: it fires once, when the window opens, so there is nothing to wait for on a
 * sale that is already live or over. The server enforces the same rule — this is
 * how the page avoids offering something that would be refused, not how the rule
 * is kept.</p>
 *
 * <p>Signed out, it points at sign-in instead of hiding: an alert has to be
 * addressed to someone, and knowing the option exists is the reason to sign
 * in.</p>
 */
export function WatchToggle({ flight }: { flight: Flight }) {
  const { session } = useSession()
  const token = session?.token ?? null

  const [watching, setWatching] = useState(false)
  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)

  // Reset during render rather than in an effect when the session or the flight
  // changes, so the toggle never shows the previous flight's (or user's) answer
  // for a frame. React's "adjust state while rendering" pattern, as in the live
  // seat map.
  const key = `${token ?? ''}:${flight.id}`
  const [checkedFor, setCheckedFor] = useState(key)
  if (checkedFor !== key) {
    setCheckedFor(key)
    setWatching(false)
    setProblem(null)
  }

  useEffect(() => {
    if (token === null) {
      return
    }

    const controller = new AbortController()

    void (async () => {
      try {
        const watched = await listWatchedFlightIds(token, controller.signal)

        if (!controller.signal.aborted) {
          setWatching(watched.includes(flight.id))
        }
      } catch {
        // An unreachable gateway leaves the toggle reading "not watching". The
        // write is idempotent, so acting on that guess cannot create a second
        // subscription — the worst case is a click that changes nothing.
      }
    })()

    return () => controller.abort()
  }, [token, flight.id])

  if (flight.saleState !== 'Upcoming') {
    return null
  }

  if (token === null) {
    return (
      <p className="text-muted-foreground text-sm">
        <Link to="/login" className="underline underline-offset-4 hover:text-foreground">
          Sign in
        </Link>{' '}
        to be told when this sale opens.
      </p>
    )
  }

  const toggle = async () => {
    setBusy(true)
    setProblem(null)

    const outcome = watching
      ? await unwatchFlight(flight.id, token)
      : await watchFlight(flight.id, token)

    setBusy(false)

    if (outcome.status === 'done') {
      setWatching(!watching)

      return
    }

    if (outcome.status === 'saleStarted') {
      // The sale opened while this page sat on screen. The server is right and
      // the page is stale; reload to see the live seat map.
      setProblem('This sale has already started — reload to book a seat.')

      return
    }

    setProblem(outcome.message)
  }

  return (
    <div className="space-y-2">
      <Button
        type="button"
        variant={watching ? 'secondary' : 'outline'}
        disabled={busy}
        aria-pressed={watching}
        onClick={() => void toggle()}
      >
        {watching ? 'Watching this sale' : 'Notify me when this sale opens'}
      </Button>

      <p className="text-muted-foreground text-sm">
        {watching
          ? 'You will be notified the moment this flash sale opens.'
          : 'Get an alert the moment this flash sale opens.'}
      </p>

      {problem !== null && (
        <p role="alert" className="text-destructive text-sm">
          {problem}
        </p>
      )}
    </div>
  )
}
