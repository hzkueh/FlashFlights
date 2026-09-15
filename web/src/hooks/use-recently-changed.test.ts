import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { useRecentlyChanged } from '@/hooks/use-recently-changed'

/**
 * What turns a live seat map's silent repaints into something a viewer can
 * follow: the seats that just moved say so, briefly, and nothing else does.
 */
describe('useRecentlyChanged', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  /**
   * The load case. Every seat arrives "new" on the first render, and flashing all
   * of them would say a hundred things changed when nothing has.
   */
  it('is quiet on first render', () => {
    const { result } = renderHook(() => useRecentlyChanged('Available'))

    expect(result.current).toBe(false)
  })

  it('reports a change the moment the value moves', () => {
    const { result, rerender } = renderHook(({ status }) => useRecentlyChanged(status), {
      initialProps: { status: 'Available' },
    })

    rerender({ status: 'Held' })

    expect(result.current).toBe(true)
  })

  it('falls quiet again once the window passes', () => {
    const { result, rerender } = renderHook(({ status }) => useRecentlyChanged(status, 600), {
      initialProps: { status: 'Available' },
    })

    rerender({ status: 'Held' })
    act(() => void vi.advanceTimersByTime(600))

    expect(result.current).toBe(false)
  })

  /**
   * A re-render is not a change. The live map re-renders every seat whenever any
   * one of them moves, so this is the common case, not the edge case.
   */
  it('ignores a re-render that carries the same value', () => {
    const { result, rerender } = renderHook(({ status }) => useRecentlyChanged(status), {
      initialProps: { status: 'Available' },
    })

    rerender({ status: 'Available' })

    expect(result.current).toBe(false)
  })

  /** A seat held and then confirmed in quick succession stays lit for the second move. */
  it('restarts the window when the value moves again mid-flash', () => {
    const { result, rerender } = renderHook(({ status }) => useRecentlyChanged(status, 600), {
      initialProps: { status: 'Available' },
    })

    rerender({ status: 'Held' })
    act(() => void vi.advanceTimersByTime(400))
    rerender({ status: 'Confirmed' })
    act(() => void vi.advanceTimersByTime(400))

    expect(result.current).toBe(true)
  })
})
