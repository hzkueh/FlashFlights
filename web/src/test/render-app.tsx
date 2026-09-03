import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router'

import { App } from '@/App'
import { SessionProvider } from '@/hooks/use-session'
import { ThemeProvider } from '@/hooks/use-theme'
import { stubFetch } from '@/test/backend'

/**
 * The whole app at a route, wrapped exactly as `main.tsx` wraps it — so a
 * provider added there and forgotten here fails the suites rather than quietly
 * making them test a different app.
 */
export function renderApp(fetchImpl: typeof fetch, route = '/') {
  stubFetch(fetchImpl)

  return render(
    <ThemeProvider>
      <SessionProvider>
        <MemoryRouter initialEntries={[route]}>
          <App />
        </MemoryRouter>
      </SessionProvider>
    </ThemeProvider>,
  )
}
