import { Route, Routes } from 'react-router'

import { AppShell } from '@/components/app-shell'
import { NotFoundPage } from '@/routes/not-found-page'
import { PlaceholderPage } from '@/routes/placeholder-page'

/**
 * The route table. Ticket 03 ships the shell and the routes; the pages
 * themselves land in the tickets each placeholder names.
 */
export function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<PlaceholderPage title="Flights" ticket="06 — catalog browsing" />} />
        <Route
          path="flights/:flightId"
          element={<PlaceholderPage title="Flight" ticket="06 — seat map" />}
        />
        <Route
          path="bookings"
          element={<PlaceholderPage title="Bookings" ticket="07 — checkout and confirmation" />}
        />
        <Route
          path="notifications"
          element={<PlaceholderPage title="Notifications" ticket="09 — watch and notify" />}
        />
        <Route path="login" element={<PlaceholderPage title="Sign in" ticket="04 — auth" />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}
