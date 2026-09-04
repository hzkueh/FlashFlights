import { Route, Routes } from 'react-router'

import { AppShell } from '@/components/app-shell'
import { LoginPage } from '@/routes/login-page'
import { NotFoundPage } from '@/routes/not-found-page'
import { PlaceholderPage } from '@/routes/placeholder-page'
import { RegisterPage } from '@/routes/register-page'

/**
 * The route table. Issue 03 ships the shell and the routes; the pages
 * themselves land in the issues each placeholder names.
 *
 * The Hold/checkout flow has no route here on purpose — issue 07 decides
 * whether it is a page or a step on the seat map, and inventing a URL for it
 * now would be a shape later work has to undo.
 */
export function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<PlaceholderPage title="Flights" issue="06 — catalog browsing" />} />
        <Route
          path="flights/:flightId"
          element={<PlaceholderPage title="Flight" issue="06 — seat map" />}
        />
        <Route
          path="bookings"
          element={<PlaceholderPage title="Bookings" issue="07 — checkout and confirmation" />}
        />
        <Route
          path="notifications"
          element={<PlaceholderPage title="Notifications" issue="09 — watch and notify" />}
        />
        <Route path="login" element={<LoginPage />} />
        <Route path="register" element={<RegisterPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}
