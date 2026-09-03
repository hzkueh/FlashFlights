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
same shape; running everything from `dotnet run` works too, because the
gateway's `web` cluster defaults to the Vite dev server. `web/README.md`
documents all three.

This closes ticket 01's handoff — "revisit when ticket 03 settles the
frontend's actual origin" — with a decision, not a deferral. The origin is
same-origin in every supported mode, so `Cors:AllowedOrigins` is not
load-bearing today. It is kept deliberately, as the one switch that lets a
separately hosted SPA reach this gateway without a code change; deleting it
would be a gateway behaviour change this ticket was not asked to make, and
ticket 04's credentialed requests are the likeliest reason to want it back.

**Three things worth knowing:**

1. *The connection indicator checks a service through the gateway, not the
   gateway's own `/health/ready`.* This is a departure from the criterion's
   literal words, taken because the gateway's own readiness covers only the
   Identity store it happens to host — it would read healthy with all three
   services down. `/api/catalog/health/ready` covers gateway routing, a real
   service, its datastore and the bus in one call, and visibly flips on
   `docker compose stop catalog`. Ticket 01's own manual-verification section
   uses that same URL.

   The cost is that it watches **one** service, so `docker compose stop
   ordering` leaves it reading Connected. Every label around it therefore names
   Catalog rather than claiming to speak for "the backend", and a test asserts
   that naming. Widening it to all three is noted as a follow-up.
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

**Deliberately not done here:** any actual screen. Every screen the spec names
has a route rendering a placeholder that names the issue filling it in — with
one exception: the Hold/checkout flow has no route, because ticket 07 decides
whether it is a page of its own or a step on the seat map, and inventing a URL
now would be a shape that ticket has to undo.

Placeholders say "Arrives in issue NN", not "ticket NN": CONTEXT.md rules
"Ticket" out of user-visible copy, since in an airline app it reads as the
boarding document rather than a work item.

### Follow-ups for later tickets

- Ticket 04 should delete the `_ping` endpoints (ticket 01's follow-up).
- `useBackendStatus` watches Catalog alone, so the header cannot show a stopped
  Ordering or Notifications. If ticket 06 or 08 wants per-service status, that
  hook is the place to widen — the badge already renders from a single
  status-keyed table.
- The SignalR connection in ticket 08 goes through the same origin; the Vite
  proxy already forwards websockets on `/api`.
