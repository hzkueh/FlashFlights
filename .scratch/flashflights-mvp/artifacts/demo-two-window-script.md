# Two-window live seat map — demo recording script (ticket 08, item 7)

A **rough** capture of the money shot: two browser windows open on the same
flight, and a Seat changing status in **both at once, with no refresh**, the
instant it is held / confirmed / released. Ticket 12 polishes this into the
README GIF — here we just get a clean take in the can.

What it demonstrates, mapped to the ticket:

- **Items 1–4** — a live seat map over SignalR, per-flight, updating both viewers.
- **Item 5** — the map recovers and re-syncs after the hub drops (bonus take).
- **Item 6** — Ordering stays authoritative: a stale hold is refused (bonus take).

> The buyer-facing hold **UI** is ticket 07 and isn't built yet, so the Seat
> change is triggered from a terminal with `curl` — i.e. the terminal plays the
> role of "another buyer". Both browser windows are pure viewers; the point is
> that the push reaches every viewer live.

All commands are written for **Git Bash** (matching the seed script), and need
`curl` + `jq`. On Windows just run them inside `bash`. Gateway is
`http://localhost:8080`.

---

## 0. Prerequisites

- **Docker Desktop running.**
- A screen recorder ready (OBS, Xbox Game Bar `Win+G`, QuickTime…).
- Two browser windows you can place side by side.

## 1. Bring the stack up and seed (one-time)

There is **no runtime seed path until ticket 11**, so an empty catalog is the
fresh state — seed three demo flights first. From the repo root (the steps come
from the seed script's docstring; see
[`seed-demo-data.py`](seed-demo-data.py)):

```bash
docker compose up -d --build
```

```bash
SP=.scratch/flashflights-mvp/artifacts
# Stop catalog BEFORE copying its SQLite out, so WAL is checkpointed in (gotcha 3).
docker compose stop catalog
docker compose cp catalog:/data/catalog.db "$SP/catalog.db"
python "$SP/seed-demo-data.py" "$SP"            # prints each flight's id — keep this output
docker compose exec -T ordering-db psql -U flashflights -d flashflights_ordering \
    -c 'TRUNCATE "SeatMovements","Bookings","Holds","Seats" CASCADE;'
docker compose exec -T ordering-db psql -U flashflights -d flashflights_ordering -v ON_ERROR_STOP=1 < "$SP/ordering.sql"
docker compose cp "$SP/catalog.db" catalog:/data/catalog.db && docker compose start catalog
```

Verify the stack answers and the seed took (three flights back, not `[]`):

```bash
curl -s http://localhost:8080/api/catalog/flights | jq -r '.[] | "\(.flightNumber) \(.saleState)"'
# FF100 Live
# FF210 Upcoming
# FF330 Ended
```

If host `curl` returns nothing / `000` while containers look healthy, the port
forward dropped on a restart — `docker compose restart gateway` and retry.

## 2. Capture the demo inputs

The **Live** flight (`FF100`, a 4×6 cabin) is the one to demo: it already has
`1A`/`1B` **Held** and `2C`/`2D` **Confirmed**, with the rest Available.

```bash
# A signed-in buyer (re-run day 2? swap /register for /login, same body).
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/register \
  -H 'content-type: application/json' \
  -d '{"email":"demo-buyer@flashflights.test","password":"DemoPass123!"}' | jq -r .token)

# The Live flight's id (Catalog stores it upper-cased — use it verbatim).
FLIGHT=$(curl -s http://localhost:8080/api/catalog/flights \
  | jq -r '.[] | select(.flightNumber=="FF100") | .id')

# Pick a clearly-visible Available seat to hold on camera, e.g. 3C.
SEAT=$(curl -s "http://localhost:8080/api/ordering/seats?flightId=$FLIGHT" \
  | jq -r '.seats[] | select(.seatNumber=="3C") | .seatId')

echo "flight=$FLIGHT  seat(3C)=$SEAT  token=${TOKEN:0:12}…"
```

All three must be non-empty before you record.

## 3. Arrange the windows

1. Open **two** browser windows, both at:
   `http://localhost:8080/flights/` + the `$FLIGHT` id from above.
2. Place them side by side, both showing the **Seat map** section.
3. Confirm they're identical: `1A`/`1B` filled (**Held**), `2C`/`2D` muted &
   struck through (**Confirmed**), everything else outlined (**Available**).
   Point the cursor at **3C** in each — outlined/Available in both. This is the
   Seat you're about to change.

Seat legend, so you can narrate the colour change:
Available = outlined · **Held** = solid fill · **Confirmed** = muted + strikethrough.

## 4. Record — the money shot

**Start recording.** Keep both windows and the terminal in frame.

**Take 1 — one hold, both windows (items 1–4).** Run:

```bash
HOLD_ID=$(curl -s -X POST http://localhost:8080/api/ordering/holds \
  -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' \
  -d "{\"flightId\":\"$FLIGHT\",\"seatIds\":[\"$SEAT\"],\"pricePerSeat\":149.00}" | jq -r .holdId)
echo "held as $HOLD_ID"
```

→ **3C flips outlined → solid fill in BOTH windows at once.** No refresh, no
click in either browser. That's the whole ticket in one beat.

**Take 2 — confirm into a booking (item 3, the "confirms" path).** Run:

```bash
curl -s -X POST "http://localhost:8080/api/ordering/holds/$HOLD_ID/confirm" \
  -H "authorization: Bearer $TOKEN" | jq '{booking: .bookingId, paid: .pricePaid}'
```

→ **3C flips fill → muted + strikethrough (Confirmed) in both windows.**

**Take 3 (bonus) — Ordering is authoritative (item 6).** Ask for `3C` again — a
Seat a lagging viewer might still think is free:

```bash
curl -i -s -X POST http://localhost:8080/api/ordering/holds \
  -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' \
  -d "{\"flightId\":\"$FLIGHT\",\"seatIds\":[\"$SEAT\"],\"pricePerSeat\":149.00}" \
  | grep -Ei 'HTTP/|seatNumber'
```

→ **`HTTP/1.1 409` naming `3C`** — the live map is advisory; the ledger refuses
the double-hold regardless of what any window shows.

**Take 4 (bonus) — release on expiry (item 3, the "expire" path).** Hold a
*fresh* Seat and leave it:

```bash
SEAT4=$(curl -s "http://localhost:8080/api/ordering/seats?flightId=$FLIGHT" \
  | jq -r '.seats[] | select(.seatNumber=="4F") | .seatId')
curl -s -X POST http://localhost:8080/api/ordering/holds \
  -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' \
  -d "{\"flightId\":\"$FLIGHT\",\"seatIds\":[\"$SEAT4\"],\"pricePerSeat\":149.00}" > /dev/null
```

→ `4F` fills (Held) in both windows; after the configured Hold TTL (~2 min) the
background sweep releases it and **both windows flip it back to outlined
(Available)** on their own. (The manual `POST /api/ordering/holds/expire` only
releases holds *already* past their TTL, so it won't shortcut this one.)

**Take 5 (bonus) — reconnect re-sync (item 5).** With both windows open, drop
the hub:

```bash
docker compose restart notifications
```

→ the connections drop and auto-reconnect; on reconnect each window re-reads the
authoritative map. Hold another Seat afterwards to show updates resume live.

**Stop recording.**

## 5. Wrap

- Keep it rough — this is the evidence take, not the polished GIF (that's ticket
  12). Trim to the ~10s where 3C changes in both windows if you want a short clip.
- Save it where ticket 12 will look (e.g. `docs/media/live-seat-map.mp4` /`.gif`).
- Reset for a clean second take by re-running the seed block in step 1.

### If a Seat doesn't move live
- Both windows on the **same** `$FLIGHT`? (compare the URL id).
- `docker compose logs -f notifications` while you hold — you should see the
  consumer relay the movement; no line means the bus/consumer isn't wired up.
- RabbitMQ healthy? `docker compose ps` — Ordering publishes and Notifications
  consumes over it.
- A frontend change won't show until `docker compose build web` (the container
  serves the bundle baked at build time).
