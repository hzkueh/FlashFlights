# 08 — Live seat map over SignalR

**What to build:** The demo moment. Two people with the same flight open watch each other's Seats change in real time — no refresh. Seat state changes in `Ordering` reach every connected viewer within a moment.

**Blocked by:** 06.

**Status:** ready-for-human

- [x] `Notifications` hosts a SignalR hub the frontend connects to.
- [x] Seat state changes published by `Ordering` are relayed to connected clients viewing that flight.
- [x] An open seat map updates without a refresh when another client holds, confirms, or lets a Hold expire.
- [x] Clients subscribe per-flight — a viewer isn't pushed changes for flights they aren't looking at.
- [x] The connection recovers after a drop, and the map re-syncs to true state on reconnect.
- [x] A stale client never renders a Seat as takeable when it isn't — `Ordering` remains authoritative and rejects the request regardless.
- [ ] **Record a rough two-window demo video as soon as this works.** Ticket 12 polishes it; do not leave creating it until then.

## Notes

Sequenced after ticket 07 rather than before it: ticket 07 (checkout) merged
first on the static seat map, so this slice both adds the live map and wires it
into the existing checkout. `SeatCheckout` now seeds from Ordering's one-off HTTP
read and keeps the grid live via `useLiveSeatMap`, so the Seats a buyer selects
from — and their own Hold's countdown — repaint as other buyers act, without a
refetch. Live updates stay advisory; Ordering's locked grant is still the sole
authority (ADR-0001), proved by the stale-client HTTP test.

The two-window demo video is still owed.
