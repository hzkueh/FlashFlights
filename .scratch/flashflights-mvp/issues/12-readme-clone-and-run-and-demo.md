# 12 — README, clone-and-run, and the demo video

**What to build:** A reviewer who has never seen the repo understands what it proves, watches it happen, and can run it themselves in one command.

**Blocked by:** 10, 11.

**Status:** ready-for-human

- [x] `git clone` + one `docker-compose up` brings up the whole working system, verified from a clean checkout.
- [x] README opens with what the project demonstrates — safe concurrent booking across services, live real-time delivery — not a feature list.
- [x] Architecture section: the three services, what each owns, gateway, event contracts, and why auth is shared rather than a fourth service.
- [x] Explains the SeatMovement ledger and links [ADR-0001](../../../docs/adr/0001-seat-movement-ledger.md), including why `Ordering` is on PostgreSQL.
- [x] **The red-then-green concurrency story from ticket 05**, with the actual failing output — the strongest single artifact in the repo.
- [x] Demo GIF/video showing two windows and a Seat changing live in both.
- [x] Screenshots of catalog, seat map, checkout, and inbox, in both themes.
- [x] Local dev instructions for running services outside compose.

## Comments

### What shipped

`README.md` at the repo root, plus nine media files under `docs/media/`.

The README leads with the claim — two buyers can never both win the same seat —
then the two-window GIF, so the live seat map is visible before any prose. The
red-then-green proof is its own section and carries the whole story: the no-op
`LockSeatsAsync`, the captured twenty-way double-hold, the `FOR UPDATE` that
fixed it, and a green run captured from the suite as it stands today rather
than quoted from the ticket-05 artifact.

Ordering of sections follows a reviewer's journey rather than the ticket's list:
thesis → GIF → what it proves → run it → the concurrency proof → architecture →
the ledger → screens → running outside compose → troubleshooting → where to read
next.

### Media

All nine files are captured from the running stack, driven through the locally
installed Chrome by Playwright (Playwright's own Chromium download is blocked
here, so `channel: 'chrome'` does the work).

- Eight screenshots: catalog, seat map, checkout, inbox, each in both themes.
  Captured by growing the viewport to the content rather than using Playwright's
  `fullPage`, which paints a sticky header at its scroll offset — the first take
  had the nav bar floating through the middle of the seat map. Trailing uniform
  background is trimmed.
- `live-seat-map.gif` — two genuine viewers on FF104, 17 frames over ~6s, 324 KB.
  A third buyer holds seat 2B and then confirms it; both panels move through
  Available → Held → Confirmed, including the ticket-10 "recently changed" ring.
  The two panels in a frame are screenshotted back to back, not simultaneously;
  the README says so rather than implying a single screen recording. A polished
  OBS take following `artifacts/demo-two-window-script.md` would be a fair swap.

### Verified from a clean checkout

A fresh `git clone` into a scratch directory, brought up as its own compose
project with its own empty volumes:

- all seven containers start and go healthy
- it self-seeds from empty: 5 flights, all three sale states, both price paths,
  FF442 at 4 of 72 seats
- the SPA is served through the gateway and renders all five flights
- `ada@flashflights.test` / `FlashDemo1` gets a token
- **the watch chain fired end to end** — FF507 crossed its `SaleStartsAt` and
  Katherine's inbox went 0 → 1 within ~18s, body `FF507 DUB to KEF is now on
  sale`, unread. Catalog's scheduler → `FlightSaleStarted` → Notifications →
  persisted inbox, on a stack that had never run before.
- `POST /holds` returns 201; the same seat again returns 409

### The one deviation, and why it is in the README

`docker compose up` verbatim **fails to bind host port 8080 on this machine**:

```
Error response from daemon: ports are not available: exposing port TCP 0.0.0.0:8080 -> 127.0.0.1:0: listen tcp 0.0.0.0:8080: bind: An attempt was made to access a socket in a way forbidden by its access permissions.
```

Windows has 8060–8159 in a WinNAT excluded port range here (`netsh int ipv4 show
excludedportrange protocol=tcp`), so this is the machine, not the compose graph
— every other container started and the gateway came up fine once republished on
9090 via the untracked override this repo already documents. The verification
above ran with that override.

It is a Windows-reviewer trap rather than a local quirk, so the README carries
it as the first troubleshooting entry, with the exact error text and both ways
out. The override snippet in the README is the one that was actually used.

### Not done

No polished screen recording — the GIF is assembled from real paired captures,
which the README states plainly. Replacing it with an OBS take before the repo
goes public is worth doing but is not blocking.
