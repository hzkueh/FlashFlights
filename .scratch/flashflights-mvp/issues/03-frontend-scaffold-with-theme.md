# 03 — Frontend scaffold with theme

**What to build:** A React + TypeScript SPA that builds and runs, styled with Tailwind + shadcn/ui, with a working light/dark toggle, that calls the gateway and shows whether the backend is reachable.

**Blocked by:** 01.

**Status:** ready-for-human

- [x] Vite + React + TypeScript app; `npm run build` and `npm run dev` both succeed.
- [x] Tailwind CSS and shadcn/ui installed and working, with at least one shadcn component rendered.
- [x] Light/dark theme toggle that persists across reloads and respects the OS default on first visit.
- [x] App calls a gateway health endpoint and displays connected/disconnected state.
- [x] Runs inside the `docker-compose` stack alongside the services, reachable through the gateway.
- [x] Basic app shell: header, theme toggle, routing set up for pages later tickets fill in.

## Comments

### Implementation notes

The SPA lives at `web/` rather than under `src/`. `src/` is the .NET build
context (`COPY src/ src/` in the root `Dockerfile`), so a frontend file landing
there would invalidate the build cache of all four service images on every
change. `web/` has its own context and `Dockerfile`; the root `.dockerignore`
excludes it from the .NET builds.

**The gateway now serves the SPA.** A `web` YARP route (`/{**catch-all}`,
`Order: 100`) points at an nginx container holding the built `dist/`, so a
reviewer opens one URL — `http://localhost:8080` — and gets both the app and
its API from the same origin. `GatewayRouteTableTests` asserts that route
table against the `appsettings.json` the gateway actually ships.

**Same-origin everywhere, so there is no base-URL setting.** Every call the
SPA makes is a root-relative path. In compose that is the gateway; under
`npm run dev` a Vite proxy sends `/api` to `localhost:8080` and reproduces the
same shape. This settles ticket 01's open question about the frontend's
origin: `Cors:AllowedOrigins` is now belt-and-braces — nothing in either
supported way of running the app needs it — and it is kept only for someone
running the SPA on a third origin.

**Three things worth knowing:**

1. *The connection indicator deliberately does not check the gateway's own
   `/health/ready`.* In this topology the gateway also serves the SPA, so a
   gateway that cannot answer cannot deliver the page that would ask — the
   badge could never legitimately read "Disconnected". It calls
   `/api/catalog/health/ready` instead, which exercises gateway routing, a real
   service, its datastore and the bus, and visibly flips on
   `docker compose stop catalog`.
2. *nginx needs `listen [::]:8080` as well as `listen 8080`.* `localhost`
   resolves to `::1` inside the container, so an IPv4-only listener serves the
   gateway fine while failing its own healthcheck — the container reports
   unhealthy while the site works. Kestrel binds both families, which is why
   this only bit the one non-.NET service.
3. *`npx shadcn init` reads `paths` from `tsconfig.json`, not
   `tsconfig.app.json`.* Without it the CLI writes components to a literal
   `@/components/` directory. The alias is declared in both files.

**Tested seams.** `resolveInitialTheme` (stored choice outranks the OS; OS
decides on a first visit) and `fetchBackendHealth` (unreachable, unhealthy, and
healthy) are unit-tested; `ThemeProvider` and the app shell are covered through
the rendered DOM. Per the spec, frontend tests are secondary — these cover the
two pieces of real logic this ticket adds, not the placeholder pages.

**Deliberately not done here:** any actual screen. Every route in the spec
exists and renders a placeholder naming the ticket that fills it in, so later
tickets replace a page rather than also wiring routing.

### Follow-ups for later tickets

- Ticket 04 should delete the `_ping` endpoints (ticket 01's follow-up) and
  decide whether `Cors:AllowedOrigins` survives at all now that nothing
  supported is cross-origin.
- `useBackendStatus` polls one service. If ticket 06 or 08 wants per-service
  status in the header, that hook is the place to widen.
- The SignalR connection in ticket 08 goes through the same origin; the Vite
  proxy already forwards websockets on `/api`.
