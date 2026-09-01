# 12 — README, clone-and-run, and the demo video

**What to build:** A reviewer who has never seen the repo understands what it proves, watches it happen, and can run it themselves in one command.

**Blocked by:** 10, 11.

**Status:** ready-for-agent

- [ ] `git clone` + one `docker-compose up` brings up the whole working system, verified from a clean checkout.
- [ ] README opens with what the project demonstrates — safe concurrent booking across services, live real-time delivery — not a feature list.
- [ ] Architecture section: the three services, what each owns, gateway, event contracts, and why auth is shared rather than a fourth service.
- [ ] Explains the SeatMovement ledger and links [ADR-0001](../../../docs/adr/0001-seat-movement-ledger.md), including why `Ordering` is on PostgreSQL.
- [ ] **The red-then-green concurrency story from ticket 05**, with the actual failing output — the strongest single artifact in the repo.
- [ ] Demo GIF/video showing two windows and a Seat changing live in both.
- [ ] Screenshots of catalog, seat map, checkout, and inbox, in both themes.
- [ ] Local dev instructions for running services outside compose.
