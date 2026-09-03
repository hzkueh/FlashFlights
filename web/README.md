# FlashFlights web

The FlashFlights SPA: React + TypeScript on Vite, Tailwind CSS v4 with
shadcn/ui, and React Router. See [`../CONTEXT.md`](../CONTEXT.md) for the
domain language the screens are named after.

## Running it

The browser is always same-origin with the gateway, so nothing here carries a
backend URL — every call the SPA makes is a root-relative path:

- **The whole stack in Docker** — `docker compose up` from the repo root, then
  <http://localhost:8080>. The gateway proxies `/` to this app's nginx
  container and `/api/*` to the services.
- **Frontend in dev, backend in Docker** — `npm run dev`, then
  <http://localhost:5173>. Vite proxies `/api` (websockets included) to the
  gateway on `localhost:8080`; `VITE_GATEWAY_ORIGIN` overrides that target.
- **Everything from `dotnet run`, no Docker** — start the services and the
  gateway locally, run `npm run dev`, and open <http://localhost:8080>. The
  gateway's own `appsettings.json` points its `web` cluster at the Vite dev
  server, so it fronts the SPA the same way it does in compose.

## Scripts

| Script              | What it does                                  |
| ------------------- | --------------------------------------------- |
| `npm run dev`       | Vite dev server with HMR                      |
| `npm run build`     | Typecheck, then production build into `dist/` |
| `npm run typecheck` | Typecheck only                                |
| `npm run test`      | Vitest, once                                  |
| `npm run lint`      | Oxlint                                        |

## Layout

```
src/
├── App.tsx          route table
├── components/      app shell, header widgets, and shadcn/ui in components/ui
├── hooks/           theme and backend-status state
├── lib/             theme resolution and the gateway client
├── routes/          one file per screen
└── test/            shared stubs for the suites above
```

Adding a shadcn component: `npx shadcn@latest add <name>`.
