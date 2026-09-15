import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'

import { App } from '@/App'
import { NotificationsProvider } from '@/hooks/use-notifications'
import { SessionProvider } from '@/hooks/use-session'
import { ThemeProvider } from '@/hooks/use-theme'
import '@/index.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <SessionProvider>
        <NotificationsProvider>
          <BrowserRouter>
            <App />
          </BrowserRouter>
        </NotificationsProvider>
      </SessionProvider>
    </ThemeProvider>
  </StrictMode>,
)
