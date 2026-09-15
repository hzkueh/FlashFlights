import { vi } from 'vitest'

/**
 * jsdom has no `matchMedia`, and two of this app's behaviours are defined
 * entirely by it — the theme's first-visit rule and whether anything animates —
 * so tests state both OS preferences explicitly rather than leaving them to a
 * global stub with an invisible default.
 */
export function stubMediaPreferences({
  prefersDark = false,
  prefersReducedMotion = false,
}: { prefersDark?: boolean; prefersReducedMotion?: boolean } = {}) {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({
      matches: query.includes('prefers-reduced-motion') ? prefersReducedMotion : prefersDark,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  )
}

/** The theme suites' shorthand: state the colour preference, leave motion on. */
export function stubPrefersDark(prefersDark: boolean) {
  stubMediaPreferences({ prefersDark })
}
