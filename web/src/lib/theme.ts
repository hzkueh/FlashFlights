/**
 * Theme resolution, kept separate from the React provider so the rule a user
 * actually notices — a stored choice outranks the OS, and the OS decides when
 * there is no stored choice — is testable without rendering anything.
 */

export type Theme = 'light' | 'dark'

/** Namespaced so it cannot collide with anything else on the gateway's origin. */
export const THEME_STORAGE_KEY = 'flashflights.theme'

const THEMES: readonly string[] = ['light', 'dark']

function asTheme(value: string | null): Theme | null {
  return value !== null && THEMES.includes(value) ? (value as Theme) : null
}

/**
 * @param stored Whatever was last persisted, or null on a first visit. Anything
 *   that is not a theme is treated as absent rather than trusted.
 * @param prefersDark The OS preference, consulted only when nothing is stored.
 */
export function resolveInitialTheme(stored: string | null, prefersDark: boolean): Theme {
  return asTheme(stored) ?? (prefersDark ? 'dark' : 'light')
}

/**
 * Applies a theme to the document. shadcn's Tailwind v4 setup keys its dark
 * palette off a `dark` class on an ancestor, so this is the one place the class
 * is written.
 */
export function applyTheme(theme: Theme, root: HTMLElement): void {
  root.classList.toggle('dark', theme === 'dark')
  root.style.colorScheme = theme
}
