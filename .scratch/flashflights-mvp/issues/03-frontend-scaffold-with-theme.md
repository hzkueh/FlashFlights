# 03 — Frontend scaffold with theme

**What to build:** A React + TypeScript SPA that builds and runs, styled with Tailwind + shadcn/ui, with a working light/dark toggle, that calls the gateway and shows whether the backend is reachable.

**Blocked by:** 01.

**Status:** ready-for-agent

- [ ] Vite + React + TypeScript app; `npm run build` and `npm run dev` both succeed.
- [ ] Tailwind CSS and shadcn/ui installed and working, with at least one shadcn component rendered.
- [ ] Light/dark theme toggle that persists across reloads and respects the OS default on first visit.
- [ ] App calls a gateway health endpoint and displays connected/disconnected state.
- [ ] Runs inside the `docker-compose` stack alongside the services, reachable through the gateway.
- [ ] Basic app shell: header, theme toggle, routing set up for pages later tickets fill in.
