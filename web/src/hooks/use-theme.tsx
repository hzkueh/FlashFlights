import { createContext, use, useCallback, useEffect, useMemo, useState } from 'react'

import { THEME_STORAGE_KEY, type Theme, applyTheme, resolveInitialTheme } from '@/lib/theme'

interface ThemeContextValue {
  theme: Theme
  toggleTheme: () => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

function prefersDark(): boolean {
  return window.matchMedia('(prefers-color-scheme: dark)').matches
}

/**
 * Owns the app's theme: resolved once from storage-then-OS, applied to the
 * document, and written back on every change so it survives a reload.
 */
export function ThemeProvider({ children }: { children: React.ReactNode }) {
  // Lazy initial state, so the first paint is already the right theme rather
  // than a flash of light followed by an effect correcting it.
  const [theme, setTheme] = useState<Theme>(() =>
    resolveInitialTheme(localStorage.getItem(THEME_STORAGE_KEY), prefersDark()),
  )

  useEffect(() => {
    applyTheme(theme, document.documentElement)
    localStorage.setItem(THEME_STORAGE_KEY, theme)
  }, [theme])

  const toggleTheme = useCallback(
    () => setTheme((current) => (current === 'dark' ? 'light' : 'dark')),
    [],
  )

  const value = useMemo(() => ({ theme, toggleTheme }), [theme, toggleTheme])

  return <ThemeContext value={value}>{children}</ThemeContext>
}

/** Throws outside a {@link ThemeProvider} rather than silently defaulting to light. */
export function useTheme(): ThemeContextValue {
  const value = use(ThemeContext)

  if (value === null) {
    throw new Error('useTheme must be used within a ThemeProvider')
  }

  return value
}
