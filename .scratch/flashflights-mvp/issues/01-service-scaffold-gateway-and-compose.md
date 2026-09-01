# 01 — Service scaffold, gateway, and compose stack

**What to build:** The plumbing, proven end to end and nothing else. A reviewer clones the repo, runs one `docker-compose up`, and every service, the gateway, the broker, and the datastores come up healthy; the gateway routes to all three services; and one service can publish an event that another observably consumes. No domain model yet — these are shells that prove the wiring.

**Blocked by:** None — can start immediately.

**Status:** ready-for-human

- [x] One .NET solution with four runnable projects (`Catalog`, `Ordering`, `Notifications`, YARP gateway) plus a test project per service; `dotnet build` succeeds.
- [x] `docker-compose up` from a clean clone brings up all services, RabbitMQ, a PostgreSQL instance for `Ordering`, and SQLite volumes for the others — no manual steps.
- [x] Services retry their datastore and broker dependencies on startup rather than crash-looping on container start order.
- [x] YARP gateway routes to all three services; a health endpoint per service is reachable through it.
- [x] CORS is configured through the gateway so a browser client can call it.
- [x] A MassTransit publish from one service is observably consumed by another (a throwaway ping event is fine) — proves the bus, not just that the container started.

## Comments

### Implementation notes

Two shared projects sit alongside the four services: `FlashFlights.Contracts`
(event records crossing service boundaries) and `FlashFlights.ServiceDefaults`
(bus connection, datastore startup retry, health endpoints). Without the latter
the same ~60 lines of host plumbing would be copy-pasted into three services.

**Verifying it by hand**, once `docker compose up` reports all six containers
healthy:

```bash
curl -s localhost:8080/api/catalog/health/ready      # 200, per-dependency detail
curl -s -X POST localhost:8080/api/catalog/_ping     # publish onto the bus
curl -s localhost:8080/api/ordering/_ping/received   # both services show the same PingId
curl -s localhost:8080/api/notifications/_ping/received
```

`_ping` / `_ping/received` and the `PingSent` contract are the throwaway wiring
probe this ticket called for. They are marked for deletion in code — remove them
once the real event contracts land.

**`/health` vs `/health/ready`.** Liveness is deliberately check-free so a
service that started before its database still answers 200 and is not restarted
out from under its own retry loop. Readiness reports the datastore and the bus,
and is what to look at when something seems wrong.

**Two things this ticket's own acceptance test caught**, both worth knowing
before ticket 02 builds on this:

1. *Consumers on the same queue.* Registering `PingSentConsumer` in both
   `Ordering` and `Notifications` initially gave them one shared `ping-sent`
   queue, so they competed and only one received each message. Endpoint names
   are now prefixed per service. Any event consumed by more than one service —
   which `SeatsHeld` will be — needs this or half the events silently vanish.
2. *Opening a pooled connection proves nothing.* The readiness probe originally
   just called `OpenAsync`, and kept reporting PostgreSQL reachable after
   `docker compose stop ordering-db`, because the pool hands back an idle
   connection without touching the server. The probes now execute `SELECT 1`.

**Deliberately not done here:** README (ticket 12), and `Ordering`'s real
PostgreSQL concurrency test infrastructure (ticket 05) — this ticket's tests are
fast and containerless.

### Follow-ups for later tickets

- The `_ping` endpoints are unauthenticated and publish on demand. Harmless
  while nothing else exists, but delete them by the time ticket 04 adds auth.
- `Cors:AllowedOrigins` defaults to `http://localhost:5173` for the Vite dev
  server; revisit when ticket 03 settles the frontend's actual origin.
