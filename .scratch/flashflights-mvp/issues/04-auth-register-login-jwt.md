# 04 — Auth: register, login, JWT across services

**What to build:** A visitor can register an account and sign in; they stay signed in across reloads; the gateway and every service validate the JWT, and protected endpoints reject unauthenticated callers.

**Blocked by:** 02, 03.

**Status:** ready-for-agent

- [ ] ASP.NET Core Identity + JWT issuance hosted as one shared lightweight piece alongside the gateway — **not** a fourth microservice, no per-service user store.
- [ ] A visitor can register with email + password and receives a JWT.
- [ ] A returning User can log in and receives a JWT.
- [ ] `Catalog`, `Ordering`, and `Notifications` all validate incoming JWTs against shared signing config.
- [ ] A protected endpoint returns 401 without a valid token and succeeds with one.
- [ ] Frontend has register and login pages, stores the token, stays signed in across reloads, and can sign out.
- [ ] Browsing the catalog and seat maps remains fully available **unauthenticated**.
