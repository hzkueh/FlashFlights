import { Route, Routes } from 'react-router'

import { AppShell } from '@/components/app-shell'
import { BookingsPage } from '@/routes/bookings-page'
import { FlightPage } from '@/routes/flight-page'
import { FlightsPage } from '@/routes/flights-page'
import { LoginPage } from '@/routes/login-page'
import { NotFoundPage } from '@/routes/not-found-page'
import { NotificationsPage } from '@/routes/notifications-page'
import { RegisterPage } from '@/routes/register-page'

/**
 * The route table. Issue 03 shipped the shell and the routes; each page has
 * since landed in the issue it named.
 *
 * The Hold/checkout flow has no route here on purpose — issue 07 decides
 * whether it is a page or a step on the seat map, and inventing a URL for it
 * now would be a shape later work has to undo.
 */
export function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<FlightsPage />} />
        <Route path="flights/:flightId" element={<FlightPage />} />
        <Route path="bookings" element={<BookingsPage />} />
        <Route path="notifications" element={<NotificationsPage />} />
        <Route path="login" element={<LoginPage />} />
        <Route path="register" element={<RegisterPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}
