# 04 — Auth: register, login, JWT across services

**What to build:** A visitor can register an account and sign in; they stay signed in across reloads; the gateway and every service validate the JWT, and protected endpoints reject unauthenticated callers.

**Blocked by:** 02, 03.

**Status:** ready-for-human

- [x] ASP.NET Core Identity + JWT issuance hosted as one shared lightweight piece alongside the gateway — **not** a fourth microservice, no per-service user store.
- [x] A visitor can register with email + password and receives a JWT.
- [x] A returning User can log in and receives a JWT.
- [x] `Catalog`, `Ordering`, and `Notifications` all validate incoming JWTs against shared signing config.
- [x] A protected endpoint returns 401 without a valid token and succeeds with one.
- [x] Frontend has register and login pages, stores the token, stays signed in across reloads, and can sign out.
- [x] Browsing the catalog and seat maps remains fully available **unauthenticated**.

## Comments

### Implementation notes

**Validation is not opt-in.** `AddFlashFlightsJwtAuthentication` is called from
`AddFlashFlightsServiceDefaults`, so a service cannot take the defaults and end
up with endpoints it believes are protected and a host that never checks a
token. The gateway calls it directly, because it takes no part in the bus and so
never takes the service defaults — and directly rather than folded into
`AddFlashFlightsIdentity`, so moving the user store elsewhere one day cannot
quietly take the gateway's ability to reject a token with it.

No host calls `UseAuthentication`/`UseAuthorization`: `WebApplication` inserts
both itself once the services are registered, and leaving it to do so is the
point — a per-host pair of calls is a per-host pair of calls to forget. The
tests' service host maps a protected endpoint and adds no middleware of its own,
so it is what would notice if that ever stopped being true.

**One key, stated once per environment.** The gateway signs, the three services
validate, and both halves read the same `JwtOptions` — validation rules are
derived from it rather than from a section of their own, so the issuing and
validating sides cannot be pointed at different keys. `JwtOptions.SigningKey`
has no default and is validated on start: a host with none configured refuses to
start rather than starting and rejecting every token it is ever sent. The
development key lives in each host's `appsettings.Development.json` (never
`appsettings.json`), and `docker-compose.yml` overrides all four from one
anchor.

**Bearer validation runs with `ClockSkew = TimeSpan.Zero`.** The default forgives
five minutes, and the same system issues and validates these tokens, so it would
only mean an expired token keeps working for five more.

**`/api/auth/me` is the one protected endpoint the gateway hosts**, and the SPA
uses it as the answer to "is this stored token still one the system accepts".
The session is restored from storage optimistically so a reload never flashes a
signed-out header, then checked behind the first render; only a 401 signs the
User out, because an unreachable gateway says nothing about whether the token is
good.

**Nothing behind the proxy is protected.** Browsing the catalog and seat maps
stays anonymous; endpoints opt in with `RequireAuthorization`, which the
Hold/Booking/Watch work in issues 05, 07, and 09 will do.

**Sign-in failures never say which half was wrong**, so the endpoint cannot be
used to enumerate registered emails. The remaining gap is timing — an unknown
email skips the hash comparison — and is noted in the handler.

### Left for later

**No test drives a shipped service's pipeline.** The 401/200 pair runs against a
host wired exactly as a service is, and `Catalog`, `Ordering`, and
`Notifications` get that wiring by taking the service defaults, which every one
of their suites already depends on. But none of them has a protected endpoint
yet, so nothing exercises theirs end to end. Issues 05, 07, and 09 add the first
real ones (Hold, Booking, Watch) and should cover it there.

**Glossary gap for `/domain-modeling`.** *Session* is now a cross-cutting
concept — `SessionResponse`, `SessionProvider`, `web/src/lib/session.ts`, and
header copy — and `CONTEXT.md` does not define it. *Credentials* and
*SignedInUser* are in the same position. Noted rather than added here, per
[domain.md](../../../docs/agents/domain.md): the glossary is resolved lazily,
when the term actually settles.
