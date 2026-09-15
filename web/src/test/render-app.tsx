import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router'

import { App } from '@/App'
import { NotificationsProvider } from '@/hooks/use-notifications'
import { SessionProvider } from '@/hooks/use-session'
import { ThemeProvider } from '@/hooks/use-theme'
import { stubFetch } from '@/test/backend'

/**
 * The whole app at a route, wrapped exactly as `main.tsx` wraps it — so a
 * provider added there and forgotten here fails the suites rather than quietly
 * making them test a different app.
 */
/**
 * No live hub in the suites that do not care about one. Throwing rather than
 * returning a stub is deliberate: `subscribeToNotifications` treats a factory
 * that cannot build a connection as "no live updates", which is exactly what a
 * jsdom test wants, and it keeps every suite from having to stub SignalR.
 */
const noLiveConnection = () => {
  throw new Error('No SignalR connection in tests.')
}

export function renderApp(fetchImpl: typeof fetch, route = '/') {
  stubFetch(fetchImpl)

  return render(
    <ThemeProvider>
      <SessionProvider>
        <NotificationsProvider createConnection={noLiveConnection}>
          <MemoryRouter initialEntries={[route]}>
            <App />
          </MemoryRouter>
        </NotificationsProvider>
      </SessionProvider>
    </ThemeProvider>,
  )
}
