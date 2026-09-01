# Spec: FlashFlights MVP

Status: ready-for-agent

_Vocabulary follows [CONTEXT.md](../../CONTEXT.md). Respects
[ADR-0001](../../docs/adr/0001-seat-movement-ledger.md) (per-seat
SeatMovement ledger)._

## Problem Statement

A portfolio built entirely around one clean .NET CRUD backend
([RestaurantInventory](../../../RestaurantInventory/CONTEXT.md)) doesn't
show what happens when several services have to agree on a fast-changing
shared fact under real concurrent load, or when a UI has to reflect that
fact live rather than on refresh. There's nothing in the portfolio proving
distributed consistency, real-time delivery, or a full SPA frontend.

## Solution

FlashFlights: buyers browse a small catalog of flights, each running a
time-boxed flash sale over a fixed pool of Seats. A live seat map shows
every Seat's SeatStatus and updates for every connected viewer the instant
it changes. A buyer selects Seats and requests a Hold, which either resolves
into a Booking (simulated payment) or expires and releases the Seats back
to the pool. The system's whole point is that this is *provably* safe under
concurrency: Ordering is the sole source of truth for SeatStatus, derived
from an append-only SeatMovement ledger inside a row-locked transaction
(ADR-0001) — no two buyers can ever hold or confirm the same Seat. Buyers
can also Watch a not-yet-live flight and get notified the instant its sale
opens. Three independently runnable services (Catalog, Ordering,
Notifications) behind a gateway, talking over an event bus, prove this
isn't a monolith wearing a microservices label.

## User Stories

1. As a visitor, I want to browse the catalog of flights running flash sales without signing in, so that I can see what's on offer before committing to an account.
2. As a visitor, I want to see each flight's remaining seat count and time left in its sale window from the catalog list, so that I can gauge urgency at a glance.
3. As a visitor, I want to open a flight's live seat map, so that I can see exactly which Seats are Available, Held, or Confirmed.
4. As a visitor, I want the seat map to update live as other people hold or confirm Seats, so that I see real availability without refreshing.
5. As a visitor, I want to register an account with email and password, so that I can hold and book Seats.
6. As a returning User, I want to sign in, so that I can access my Bookings and Watches.
7. As a User, I want to stay signed in across page loads via a JWT, so that I don't re-authenticate on every action.
8. As a User, I want to sign out, so that I can end my session.
9. As a User, I want to select one or more Available Seats on a flight's seat map, so that I can attempt to buy them.
10. As a User, I want to request a Hold on my selected Seats, so that they're reserved for me while I complete checkout.
11. As a User, I want my Hold request to fail cleanly and tell me which Seats were unavailable if another buyer already holds or confirmed one of them, so that I'm never shown a false success.
12. As a User, I want to see a live countdown of my Hold's remaining time, so that I know how long I have to complete checkout.
13. As a User, I want to confirm my Hold via a simulated payment step, so that it becomes a Booking.
14. As a User, I want an expired, unconfirmed Hold to automatically release its Seats back to Available, so that abandoned checkouts don't lock up inventory forever.
15. As a User, I want to see my Booking confirmation with the Seats, flight, and price paid, so that I have proof of purchase.
16. As a User, I want to see a history of my past Bookings, so that I can review what I've bought.
17. As a User, I want it to be impossible for two buyers to both successfully hold or confirm the same Seat, even under simultaneous requests, so that I never lose a Seat I thought I'd secured.
18. As a User, I want to Watch a specific flight that hasn't gone on sale yet, so that I'm notified the moment its flash sale opens.
19. As a User, I want to receive a live in-app Notification when a Watched flight's sale goes live while I'm connected, so that I can act immediately.
20. As a User, I want Notifications to also land in a persisted inbox, so that I still see them if I wasn't connected when they fired.
21. As a User, I want to stop watching a flight, so that I don't keep getting notified about something I'm no longer interested in.
22. As a User, I want to toggle light/dark theme, so that I can use the app comfortably in either.
23. As a User, I want smooth, purposeful animations on the seat map and countdowns rather than jarring instant changes, so that the live-updating feel is legible, not chaotic.
24. As a reviewer, I want realistic seed data covering live, upcoming, and already-ended flash sales, so that every screen and state is demonstrable without manual setup.
25. As a reviewer, I want to clone the repo and bring the whole system up with a single `docker-compose up`, so that I can evaluate it without a multi-step manual setup.
26. As a reviewer, I want a README covering architecture, run steps, and a demo GIF/video, so that I can understand the design before reading code.
27. As a reviewer, I want to open two browser windows and watch a Seat disappear from one the moment I hold it in the other, so that I can see the concurrency guarantee for myself, not just take it on faith.
28. As a reviewer, I want Catalog, Ordering, and Notifications to be independently runnable services communicating over defined contracts (HTTP via the gateway, async events over the bus), so that the system demonstrably isn't a monolith wearing a microservices label.

## Implementation Decisions

**Scope:** One spec covering the whole FlashFlights MVP.

**Stack:** Backend split into three .NET services — `Catalog`, `Ordering`,
`Notifications` — behind a YARP API gateway. Frontend: React + TypeScript
SPA, Tailwind CSS + shadcn/ui, light/dark theme, Framer Motion for the seat
map and countdown animations. Async inter-service messaging via
MassTransit + RabbitMQ. Each service owns its own datastore. **`Ordering`
uses PostgreSQL, not SQLite** — its no-double-hold guarantee depends on real
row-level locking (`SELECT ... FOR UPDATE`), which SQLite cannot express:
SQLite serialises all writers behind a single database-level lock, so a
concurrency test against it would pass for the wrong reason and prove
nothing. `Catalog` and `Notifications` have no such requirement and use
SQLite. Otherwise lean infra: no Kubernetes, no service discovery, no
per-service observability stack. Whole system orchestrated locally via
`docker-compose`.

**Auth:** ASP.NET Core Identity + JWT issuance, hosted as one shared,
lightweight piece alongside the gateway — not a fourth microservice.
`Catalog`, `Ordering`, and `Notifications` validate incoming JWTs against
shared signing config; no per-service user store. Browsing the catalog and
seat maps is unauthenticated; requesting a Hold, confirming a Booking, and
Watching a flight require a signed-in User.

**Primary seam — `Ordering`'s `IHoldService`:** `CreateHold(flightId,
seatIds, userId)`, `ConfirmHold(holdId)`, plus a background `ExpireHolds`
sweep. The highest-value seam in the system — it's where ADR-0001's
no-double-hold guarantee is actually enforced, inside a row-locked
transaction over the SeatMovement ledger. Everything above it (API
controllers, the gateway) is a thin caller.

**Second seam — `Catalog`'s `IFlightCatalogService`:** `ListFlights()`,
`GetFlight(id)`, backed by an eventually-consistent projection that
consumes `SeatsHeld`/`SeatsReleased`/`SeatsConfirmed` events published by
`Ordering`. This projection is read-only decoration for browsing —
`Ordering` never consults it when granting a Hold.

**Third seam — `Notifications`'s `IWatchNotificationDispatcher`:**
`OnFlightSaleStarted(flightId)` looks up matching Watches, creates
Notification records, and pushes to connected clients over the SignalR
hub. `Catalog` runs the scheduler that detects a flight crossing its
`SaleStartsAt` and publishes `FlightSaleStarted`.

**Domain model:** Flight, Seat, SeatMovement (`Held | Released |
Confirmed`), SeatStatus (computed from the latest SeatMovement, never
stored), Hold (a set of Seats under an unresolved `Held` movement with a
TTL, status computed), Booking, Watch, Notification, User — as defined in
[CONTEXT.md](../../CONTEXT.md).

**Hold TTL:** short, on the order of 2 minutes. `Ordering`'s background
sweep finds unresolved Holds past their TTL and posts a compensating
`Released` SeatMovement for each of their Seats.

**Payment stub:** confirming a Hold runs a simulated payment step that
always succeeds instantly — no real payment integration, no artificial
failure injection. Payment processing is explicitly out of scope; the
point being tested is the Hold/ledger mechanics, not checkout UX.

**Event contracts:** `Ordering` publishes `SeatsHeld` / `SeatsReleased` /
`SeatsConfirmed` (consumed by `Catalog`'s projector). `Catalog` publishes
`FlightSaleStarted` (consumed by `Notifications`).

**Screens (MVP):** Catalog list (live seat counts + countdowns) · Flight
detail with live seat map · Hold/checkout flow with countdown + confirm ·
Booking confirmation + booking history · Watch toggle on upcoming flights ·
Notification inbox · Register/Login · theme toggle.

## Testing Decisions

**What makes a good test here:** exercises externally observable behaviour
through a seam, not implementation details. For `IHoldService` specifically,
sequential-logic unit tests are necessary but not sufficient — the actual
claim FlashFlights makes is a concurrency guarantee, so it must also be
tested under genuine simultaneous access, against a real test database
transaction (not an in-memory fake), the same way the guarantee will
actually run in production.

**Primary target — `IHoldService`:**
- Sequential tests: `CreateHold` → `ConfirmHold` produces a Booking with
  the right Seats and price; `CreateHold` → left to expire releases the
  Seats back to Available; a resolved Hold cannot be re-confirmed or
  re-held; requesting a Hold on an already-Held or Confirmed Seat fails
  cleanly and names the conflicting Seats.
- Concurrency tests: fire N parallel `CreateHold` calls at the same Seat
  (or overlapping Seat sets) and assert exactly one succeeds and the rest
  receive a clean conflict — this is the test that actually proves
  ADR-0001's guarantee, mirroring RestaurantInventory's ledger unit tests
  but extended to real parallel access, which is the specific new claim
  this project makes. These run against a **real PostgreSQL instance**
  (containerised for the test run), never an in-memory provider — an
  in-memory or SQLite database cannot exhibit the race the test exists to
  rule out.
- The concurrency test must be **demonstrated red before it goes green**:
  write the naive unlocked implementation first, observe it produce a
  double-hold, then fix it. A test that has never failed is not evidence
  the guarantee holds, and the red-then-green pair is itself the most
  compelling thing to show in the README.

**Second target — `Catalog`'s projector:** feed it
`SeatsHeld`/`SeatsReleased`/`SeatsConfirmed` events and assert the cached
seat count updates correctly; a separate assertion confirms `Ordering`'s
`IHoldService` tests never depend on this projection being present or
correct.

**Third target — `IWatchNotificationDispatcher`:** given a set of Watches
and a `FlightSaleStarted` event, assert the right Notification records are
created and a fake `IHubContext` receives pushes only for currently
connected Users.

**Frontend:** component/integration tests for the seat map's live-update
rendering are welcome but secondary — the concurrency proof lives in the
backend seams above, the frontend is a thin, real-time caller of them.

**Prior art:** RestaurantInventory's StockMovement ledger unit tests are
the direct prior art for `SeatMovement` sequential tests. There is no prior
art in either repo for concurrent-access tests — this establishes the
pattern.

## Out of Scope

Per [CONTEXT.md](../../CONTEXT.md) "Out of scope" and the scope decisions
made while grilling this spec:
- Quantity-only ticket purchasing (no seat selection) — superseded by the
  interactive seat map.
- Admin-authored flights or any admin panel — seed data only.
- Real payment processing — a stubbed, always-succeeds confirm step only.
- Cancellations or refunds on a confirmed Booking.
- Email or push notifications — in-app live delivery + persisted inbox
  only.
- Low-stock / sold-out Watch triggers — a Watch fires only on sale-goes-
  live; other triggers are a possible later stretch.
- Multiple flash-sale windows per Flight.
- A dedicated Identity microservice — auth is one shared, lightweight
  piece, not its own service.
- Kubernetes, service discovery, or a per-service observability stack —
  lean infra only, orchestrated via `docker-compose`.
- Live cloud deployment — a required deliverable is a local
  `docker-compose up` demo plus a recorded GIF/video in the README; live
  hosting is optional stretch only, not part of this MVP's definition of
  done.

## Further Notes

- FlashFlights is a deliberate companion to RestaurantInventory, not a
  repeat of it: the append-only ledger pattern is reused on purpose
  (ADR-0001 cross-references RestaurantInventory's ADR-0001), this time
  proven under real cross-service concurrency and delivered live via
  SignalR, behind a full React SPA — different skills than the first
  piece, on purpose.
- Time budget: 1-2 weeks, solo. If it runs short, cut from the bottom of
  this priority order: 1) core browse→hold→confirm flow with live,
  authoritative seat counts (the actual proof-of-concept), 2) SignalR live
  updates across clients, 3) real auth, 4) Watch/Notification, 5) visual
  polish (dark mode, animations). The README demo GIF/video is not
  optional regardless of what else gets cut — treat it as part of the MVP,
  not a stretch goal.
- Implementation issues for this spec go one file per ticket under
  `.scratch/flashflights-mvp/issues/`, numbered from `01`, per
  [issue-tracker.md](../../docs/agents/issue-tracker.md).
