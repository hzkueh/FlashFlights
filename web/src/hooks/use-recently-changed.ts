import { useEffect, useState } from 'react'

/** Long enough to notice, short enough that a busy map settles. */
const DEFAULT_WINDOW_MS = 600

/**
 * Whether `value` has changed within the last `windowMs` — the signal behind the
 * seat map's change highlight.
 *
 * A live map repaints every seat whenever any one of them moves, so "what is
 * different" cannot be read from a render; it has to be remembered. This
 * remembers it per-seat, and deliberately reports `false` on the first render: on
 * load every seat is new, and flashing the whole cabin would say a hundred things
 * changed when nothing has.
 *
 * The comparison is `Object.is`, so this suits the primitives it is used for (a
 * SeatStatus); a caller holding an object should pass something derived and
 * stable instead. Updating state during render is React's documented "adjust
 * state while rendering" pattern, used here for the same reason the live seat map
 * and the watch toggle use it — so a change is reflected in the frame it arrives
 * in, not one frame later.
 */
export function useRecentlyChanged<T>(value: T, windowMs = DEFAULT_WINDOW_MS): boolean {
  const [seen, setSeen] = useState(value)
  const [changed, setChanged] = useState(false)
  // Counts the moves rather than timing them: two changes inside the same
  // millisecond are still two, and the window restarts for the second.
  const [move, setMove] = useState(0)

  if (!Object.is(seen, value)) {
    setSeen(value)
    setChanged(true)
    setMove((count) => count + 1)
  }

  useEffect(() => {
    if (!changed) {
      return
    }

    const timer = setTimeout(() => setChanged(false), windowMs)

    return () => clearTimeout(timer)
  }, [changed, move, windowMs])

  return changed
}
