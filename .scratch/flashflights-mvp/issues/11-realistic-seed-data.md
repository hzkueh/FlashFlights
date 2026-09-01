# 11 — Realistic seed data

**What to build:** A fresh clone comes up populated, so every screen and every state is demonstrable immediately without clicking anything into existence.

**Blocked by:** 06, 07, 09.

**Status:** ready-for-agent

- [ ] Seeded flights spanning **upcoming**, **live**, and **ended** sale windows so all three states are visible on first run.
- [ ] Realistic routes, departure times, and flash prices — not `Flight 1`, `Flight 2`.
- [ ] Seat maps of a believable size with a varied mix of Available, Held, and Confirmed Seats.
- [ ] At least one flight nearly sold out, and one with an imminent `SaleStartsAt` so the Watch notification can be demoed live in under a minute.
- [ ] Seeded demo Users with existing Bookings and Watches, so booking history and the inbox aren't empty on a fresh look.
- [ ] Seed runs automatically on first startup and is idempotent across restarts.
