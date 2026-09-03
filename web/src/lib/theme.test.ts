import { describe, expect, it } from 'vitest'

import { resolveInitialTheme } from '@/lib/theme'

describe('resolveInitialTheme', () => {
  it('falls back to the OS preference on a first visit', () => {
    expect(resolveInitialTheme(null, true)).toBe('dark')
    expect(resolveInitialTheme(null, false)).toBe('light')
  })

  it('prefers a stored choice over the OS preference', () => {
    expect(resolveInitialTheme('light', true)).toBe('light')
    expect(resolveInitialTheme('dark', false)).toBe('dark')
  })

  it('ignores a stored value that is not a theme', () => {
    expect(resolveInitialTheme('chartreuse', true)).toBe('dark')
  })
})
