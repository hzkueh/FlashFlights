# 09 — Watch a flight, get notified when its sale opens

**What to build:** A User can Watch an upcoming flight and is told the moment its flash sale goes live — pushed instantly if they're connected, waiting in their inbox if they weren't.

**Blocked by:** 04, 08.

**Status:** ready-for-agent

- [ ] Signed-in User can Watch and un-Watch a flight whose sale hasn't started.
- [ ] `Catalog` runs a scheduler that detects a Flight crossing its `SaleStartsAt` and publishes `FlightSaleStarted` exactly once per flight.
- [ ] `IWatchNotificationDispatcher` consumes it, finds matching Watches, and creates a `Notification` per watching User.
- [ ] Connected Users receive the Notification live over the existing SignalR hub.
- [ ] Notifications persist to an inbox and are visible on next sign-in whether or not the User was connected.
- [ ] Notifications can be marked read; unread count is visible in the app shell.
- [ ] Un-Watching stops future notifications for that flight.
