import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ThemeProvider, useTheme } from '@/hooks/use-theme'
import { THEME_STORAGE_KEY } from '@/lib/theme'
import { stubPrefersDark } from '@/test/matchMedia'

function ThemeProbe() {
  const { theme, toggleTheme } = useTheme()

  return (
    <button type="button" onClick={toggleTheme}>
      {theme}
    </button>
  )
}

function renderProbe() {
  return render(
    <ThemeProvider>
      <ThemeProbe />
    </ThemeProvider>,
  )
}

beforeEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
})

afterEach(() => vi.unstubAllGlobals())

describe('ThemeProvider', () => {
  it('respects the OS preference on a first visit', () => {
    stubPrefersDark(true)

    renderProbe()

    expect(screen.getByRole('button')).toHaveTextContent('dark')
    expect(document.documentElement).toHaveClass('dark')
  })

  it('restores the stored choice over the OS preference, so it survives a reload', () => {
    stubPrefersDark(true)
    localStorage.setItem(THEME_STORAGE_KEY, 'light')

    renderProbe()

    expect(screen.getByRole('button')).toHaveTextContent('light')
    expect(document.documentElement).not.toHaveClass('dark')
  })

  it('persists the choice when toggled', async () => {
    stubPrefersDark(false)

    renderProbe()
    await userEvent.click(screen.getByRole('button'))

    expect(screen.getByRole('button')).toHaveTextContent('dark')
    expect(document.documentElement).toHaveClass('dark')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
  })
})
