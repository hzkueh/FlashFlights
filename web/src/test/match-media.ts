import { vi } from 'vitest'

/**
 * jsdom has no `matchMedia`, and the theme's first-visit rule is defined
 * entirely by it — so tests state the OS preference explicitly rather than
 * leaving it to a global stub with an invisible default.
 */
export function stubPrefersDark(prefersDark: boolean) {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({
      matches: query.includes('dark') ? prefersDark : false,
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
