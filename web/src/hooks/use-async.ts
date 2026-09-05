import { useEffect, useState } from 'react'

/**
 * Loads something from the backend and tracks the three states a page has to
 * render: still loading, loaded, or failed. Re-runs whenever `key` changes (a
 * flight id, say) and aborts the in-flight request when it does or on unmount,
 * so a stale answer can never overwrite a newer one.
 *
 * `key` alone drives re-runs: a page passes a freshly-built loader closure each
 * render, and only a changed key restarts the request — the same shape
 * `useBackendStatus` uses, so it stays lint-clean and predictable.
 */
export type AsyncState<T> =
  | { status: 'loading'; data: null; error: null }
  | { status: 'ready'; data: T; error: null }
  | { status: 'error'; data: null; error: Error }

const LOADING = { status: 'loading', data: null, error: null } as const

export function useAsync<T>(load: (signal: AbortSignal) => Promise<T>, key: string): AsyncState<T> {
  const [state, setState] = useState<AsyncState<T>>(LOADING)

  useEffect(() => {
    const controller = new AbortController()

    async function run() {
      setState(LOADING)

      try {
        const data = await load(controller.signal)
        if (!controller.signal.aborted) {
          setState({ status: 'ready', data, error: null })
        }
      } catch (error) {
        // An abort is us cancelling a superseded request, not a failure to show.
        if (!controller.signal.aborted) {
          setState({ status: 'error', data: null, error: error as Error })
        }
      }
    }

    void run()

    return () => controller.abort()
    // Only `key` should restart the load; the loader closure is rebuilt each
    // render by design and is read fresh here without being a dependency.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])

  return state
}
