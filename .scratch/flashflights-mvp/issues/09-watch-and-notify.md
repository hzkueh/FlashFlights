# 09 — Watch a flight, get notified when its sale opens

**What to build:** A User can Watch an upcoming flight and is told the moment its flash sale goes live — pushed instantly if they're connected, waiting in their inbox if they weren't.

**Blocked by:** 04, 08.

**Status:** ready-for-human

- [x] Signed-in User can Watch and un-Watch a flight whose sale hasn't started.
- [x] `Catalog` runs a scheduler that detects a Flight crossing its `SaleStartsAt` and publishes `FlightSaleStarted` exactly once per flight.
- [x] `IWatchNotificationDispatcher` consumes it, finds matching Watches, and creates a `Notification` per watching User.
- [x] Connected Users receive the Notification live over the existing SignalR hub.
- [x] Notifications persist to an inbox and are visible on next sign-in whether or not the User was connected.
- [x] Notifications can be marked read; unread count is visible in the app shell.
- [x] Un-Watching stops future notifications for that flight.

## Notes

Split across the two services the spec names. `Catalog` owns the scheduler: a
background loop spots a Flight crossing its `SaleStartsAt` and publishes
`FlightSaleStarted` on the bus. Announced-once rests on a new
`Flight.SaleStartHandledAt` marker, written *after* a successful publish — so the
failure mode is a duplicate rather than a silence, and the duplicate is absorbed
by Notifications' per-(User, Flight) unique inbox. Strictly that makes the
publish at-least-once and the *effect* exactly-once; the alternative (mark first)
would trade a harmless duplicate for an alert nobody can recover.

Every crossing is announced, including a window that had already closed when it
was first seen — an already-ended sale in the seed data ticket 11 will add, or a
service down across the whole window. Catalog does not decide who hears about it:
the announcement carries `SaleEndsAt`, and Notifications turns a closed window
into "tell no one" while still recording that the sale's moment has passed.
Judging it in Catalog instead left Notifications unable to tell an ended sale
from one that has not opened, and so willing to accept a Watch that could never
fire — a hole the code review caught.

Unlike Ordering's best-effort seat-movement publish, a failed announcement is
surfaced rather than swallowed: the seat map's advisory counts heal on the next
movement, but a lost alert has no second source.

`Notifications` gained a second SignalR hub beside the seat map's. The seat-map
hub is anonymous and grouped per Flight; this one is authenticated and addressed
per User, so an alert reaches exactly the person it belongs to and there is
nothing for a client to subscribe to. A `FlashFlightsUserIdProvider` names a
connection by the raw `sub` claim — without it every per-User push would silently
reach no one — and the bearer scheme reads the token off the WebSocket
handshake's `access_token` query parameter for that path alone, since a browser
cannot set a header on a handshake.

One design call worth naming: "a flight whose sale hasn't started" is enforced
server-side without Notifications ever asking Catalog. A `SaleAnnouncement` row,
written by the dispatcher whenever it consumes an announcement, is how this
service knows a sale has opened — the same shape as the seat-map relay reading a
Seat's new status straight off the movement event. A Watch arriving after that is
refused with a 409. It is bookkeeping rather than new domain language, so it is
recorded in the **Watch** entry of `CONTEXT.md` rather than as a term of its own.

Two deliberate departures from the ticket's wording, both argued above: item 2's
"exactly once" is at-least-once publish plus idempotent consume, and item 4's
"the existing SignalR hub" is a second, authenticated hub rather than the
anonymous seat-map one.

The alert is persisted before it is pushed, so the inbox is the promise and the
push is only how a connected User hears it sooner. The SPA holds one inbox above
the router: the app shell's unread badge and the notifications page read the same
state, so they cannot disagree.

This replaced the last `PlaceholderPage` route, so that component is gone.

Verified against the running stack as well as the suites: a flight seeded to open
two minutes out was watched over the API, and the alert reached an already-open
page — the unread badge went to 1 with no second inbox fetch, so it arrived over
the hub rather than a poll. Watching an already-live sale returned 409, and the
inbox endpoint refused an unauthenticated caller.
