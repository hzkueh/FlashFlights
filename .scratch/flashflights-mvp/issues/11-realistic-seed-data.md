# 11 — Realistic seed data

**What to build:** A fresh clone comes up populated, so every screen and every state is demonstrable immediately without clicking anything into existence.

**Blocked by:** 06, 07, 09.

**Status:** ready-for-human

- [x] Seeded flights spanning **upcoming**, **live**, and **ended** sale windows so all three states are visible on first run.
- [x] Realistic routes, departure times, and flash prices — not `Flight 1`, `Flight 2`.
- [x] At least one flight carries a `ReferenceFare` above its `FlashPrice` (so the "Save X%" badge is demoed) and at least one carries none (so the plain-price, no-saving path is demoed too) — `ReferenceFare` is the exception, not the default.
- [x] Seat maps of a believable size with a varied mix of Available, Held, and Confirmed Seats.
- [x] At least one flight nearly sold out, and one with an imminent `SaleStartsAt` so the Watch notification can be demoed live in under a minute.
- [x] Seeded demo Users with existing Bookings and Watches, so booking history and the inbox aren't empty on a fresh look.
- [x] Seed runs automatically on first startup and is idempotent across restarts.

## Comments

### What shipped

`FlashFlights.DemoData` describes one demo world declaratively; each of the four
services projects the part its own store owns. Shared ids are derived from names
rather than generated, so the services agree without coordinating or seeding in
any particular order. See
[ADR-0004](../../../docs/adr/0004-one-demo-world-seeded-by-each-service-into-its-own-store.md).

Five flights: **FF104** LHR→BCN (live, ReferenceFare 229 → 149, all three seat
statuses), **FF221** SFO→JFK (upcoming, no saving, untouched cabin), **FF318**
CDG→FCO (ended), **FF442** AMS→LIS (live, 4 of 72 seats left), **FF507** DUB→KEF
(upcoming, opens 45s after the seed — the watch demo).

Demo buyers: `ada@`, `grace@`, `alan@`, `katherine@flashflights.test`, all with
the password `FlashDemo1`. Ada is the one worth signing in as — two bookings,
three watches, two notifications with one unread. `DemoSeed:Enabled=false` turns
the whole seed off.

The rest of the nearly-sold-out cabin belongs to buyers with no account. Dealing
those sixty-six seats among four demo buyers would have given each of them eight
separate bookings on one flight, which reads like a bug; a UserId is a
cross-service reference and never an FK (ADR-0001), so it costs nothing for most
passengers to be people you cannot sign in as.

### Verified on a live stack

Against a throwaway `docker compose` project with its own volumes:

- list page shows Upcoming / Live / Ended, both price paths, and "4 of 72 seats left"
- FF104's seat map renders 99 Available, 3 Held (7C, 7D, 14F), 6 Confirmed
- signing in as Ada returns her two bookings, three watches, and an inbox with one unread
- Katherine's watch on FF507 fired **49 seconds** after the stack came up, pushed and stored
- `POST /holds` returns 201 on both live flights — the announcement path reaches
  Ordering from seeded flights, which is the ADR-0003 case
- a full `docker compose restart` leaves every flight and count byte-identical,
  and each store logs "seeded" exactly once

### Left for later

`artifacts/seed-demo-data.py` is the hand-injection stopgap this ticket replaces.
It is kept for the gotchas documented in its docstring, with a note at the top
saying not to run it against a stack that now seeds itself — running it would
clobber the seeded world and leave Catalog and Ordering disagreeing.
