import { useEffect, useState } from 'react'

import { type BackendHealth, fetchBackendHealth } from '@/lib/gateway'

export type BackendStatus = 'checking' | 'connected' | 'disconnected'

export interface BackendState {
  status: BackendStatus
  health: BackendHealth | null
}

/** Slow enough to be a heartbeat rather than traffic; fast enough to notice a restart. */
const POLL_MS = 15_000

/**
 * Polls so the header reflects the watched service going away, not just whether
 * it was there when the page loaded — which is the state a reviewer running
 * `docker compose stop catalog` actually wants to see.
 */
export function useBackendStatus(): BackendState {
  const [state, setState] = useState<BackendState>({ status: 'checking', health: null })

  useEffect(() => {
    const controller = new AbortController()

    async function check() {
      try {
        const health = await fetchBackendHealth(controller.signal)
        setState({ status: 'connected', health })
      } catch {
        if (!controller.signal.aborted) {
          setState({ status: 'disconnected', health: null })
        }
      }
    }

    void check()
    const timer = setInterval(() => void check(), POLL_MS)

    return () => {
      controller.abort()
      clearInterval(timer)
    }
  }, [])

  return state
}
