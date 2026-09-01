# 01 — Service scaffold, gateway, and compose stack

**What to build:** The plumbing, proven end to end and nothing else. A reviewer clones the repo, runs one `docker-compose up`, and every service, the gateway, the broker, and the datastores come up healthy; the gateway routes to all three services; and one service can publish an event that another observably consumes. No domain model yet — these are shells that prove the wiring.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] One .NET solution with four runnable projects (`Catalog`, `Ordering`, `Notifications`, YARP gateway) plus a test project per service; `dotnet build` succeeds.
- [ ] `docker-compose up` from a clean clone brings up all services, RabbitMQ, a PostgreSQL instance for `Ordering`, and SQLite volumes for the others — no manual steps.
- [ ] Services retry their datastore and broker dependencies on startup rather than crash-looping on container start order.
- [ ] YARP gateway routes to all three services; a health endpoint per service is reachable through it.
- [ ] CORS is configured through the gateway so a browser client can call it.
- [ ] A MassTransit publish from one service is observably consumed by another (a throwaway ping event is fine) — proves the bus, not just that the container started.
