import { useEffect, useState } from 'react'

/**
 * A clock that re-renders its user on a fixed cadence, so a countdown ticks
 * down without the page re-fetching. One second by default — a countdown is the
 * one place a buyer expects the number to move on its own.
 *
 * Deliberately plain: the seat map's live updates (ticket 08) come over SignalR,
 * not from polling, so this only advances local time, never re-reads the server.
 */
export function useNow(intervalMs = 1000): Date {
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs)

    return () => clearInterval(timer)
  }, [intervalMs])

  return now
}
