# FlashFlights web

The FlashFlights SPA: React + TypeScript on Vite, Tailwind CSS v4 with
shadcn/ui, and React Router. See [`../CONTEXT.md`](../CONTEXT.md) for the
domain language the screens are named after.

## Running it

The SPA is same-origin with the gateway everywhere it runs, so nothing here
carries a backend URL:

- **In the compose stack** — `docker compose up` from the repo root, then
  <http://localhost:8080>. The gateway proxies `/` to this app's nginx
  container and `/api/*` to the services.
- **In dev** — `npm run dev`, then <http://localhost:5173>. Vite proxies
  `/api` and `/health` to the gateway on `localhost:8080`
  (`VITE_GATEWAY_ORIGIN` overrides that target).

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
└── routes/          one file per screen
```

Adding a shadcn component: `npx shadcn@latest add <name>`.
