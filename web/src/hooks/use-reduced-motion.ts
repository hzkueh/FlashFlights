import { useEffect, useState } from 'react'

export const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)'

/**
 * Whether this reader has asked the OS for less motion — the switch every
 * animated component in the app checks before it moves.
 *
 * Hand-rolled rather than taken from Framer Motion, whose own hook caches the
 * media query in a module-level singleton: the first test file to touch it would
 * pin the preference for every file after it, which is exactly the kind of
 * invisible cross-suite coupling a stubbed `matchMedia` is meant to avoid. This
 * reads the query on mount and follows it, so a test states the preference and a
 * reader who changes it mid-session is obeyed without a reload.
 *
 * The CSS side is handled separately, in `index.css` — this covers what Framer
 * Motion animates as inline styles, which a stylesheet cannot reach.
 */
export function useReducedMotion(): boolean {
  const [reduced, setReduced] = useState(() => window.matchMedia(REDUCED_MOTION_QUERY).matches)

  useEffect(() => {
    const query = window.matchMedia(REDUCED_MOTION_QUERY)

    const sync = () => setReduced(query.matches)
    sync()
    query.addEventListener('change', sync)

    return () => query.removeEventListener('change', sync)
  }, [])

  return reduced
}
