import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

/**
 * jsdom has no `matchMedia`, and this app asks it two questions — which theme to
 * start in, and whether to animate — so a window without it is one a render can
 * crash against.
 *
 * Installed here as a plain assignment rather than a stub, which matters: a suite
 * that stubs its own preferences in `beforeEach` and calls `vi.unstubAllGlobals`
 * in `afterEach` would otherwise leave the property *deleted* between tests, and
 * anything rendering in that gap (React flushing a pending effect, a countdown
 * still ticking) would fail for a reason that has nothing to do with the suite it
 * fails in. Restoring to this baseline instead means the answer is always "no
 * preference stated" unless a test says otherwise — see `@/test/match-media`.
 */
window.matchMedia = (query: string) =>
  ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  }) as MediaQueryList

afterEach(cleanup)
