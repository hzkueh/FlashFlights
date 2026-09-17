# One demo world, seeded by each service into its own store

A fresh clone has to come up demonstrable: flights on the list page, seats in
every state on the seat map, a buyer with a booking history and an inbox
(ticket 11). Nothing else can put them there — there is no flight-authoring UI
and no admin role (CONTEXT.md), so before this the only data that ever existed
was what tests made, or what someone injected into the running containers by
hand.

The data is not one service's to own. A Flight lives in Catalog; its Seats and
their ledger live in Ordering; a Watch on it lives in Notifications; the buyer
who holds a Seat lives in the gateway's Identity store. Every reference across
those is a plain value and never an FK (ADR-0001), so four stores have to agree
about ids that nothing would catch them disagreeing about.

**`FlashFlights.DemoData` describes the world once, declaratively, and each
service projects the part of it that its own store owns.** No seeder reads
another store, and none of them coordinate: every shared id is *derived from a
name* rather than generated, so Catalog's `FF104`, Ordering's Seats for it, and
Notifications' Watch on it arrive at the same Guid independently and in any
order.

## Why derived ids rather than a seeding order

The alternatives were a shared table of ids, or seeding the services in sequence
and passing ids along. Both reintroduce, for demo data, exactly the coupling the
rest of the system spends its design avoiding: a startup order between services
that are otherwise free to come up in any order and retry (docker-compose says
so in as many words). Derivation makes the question local — a service can answer
"which Guid is FF104?" alone, offline, on any machine, forever.

The scheme is a name-based UUIDv8 (RFC 9562 §5.8): SHA-256 over a fixed
namespace and the name, truncated with the version and variant bits set. The
namespace and the derivation are pinned by a test, because changing either
silently relabels every seeded row.

## Why each seeder fills only an empty store

A seeder writes if and only if the store holds nothing of the kind it owns — no
Flights, no Seats, no Watches, no Users. That is the whole of "seeds on first
startup and is idempotent across restarts": there is no merge, no upsert, and no
reconciliation of a world that has drifted.

It is also the safer rule. A store someone has already used is not one to pour a
demo into, and the Identity store is the sharp case — the demo buyers share a
password printed in this repository, which is right for a laptop and wrong
anywhere else. `DemoSeed:Enabled` turns the whole thing off without a code
change.

Each seeder writes in one transaction, because it runs inside the startup retry
loop: a seed that failed halfway would otherwise leave rows behind for the next
attempt to mistake for a finished job.

## Why the seeders do not write sale announcements

Ordering and Notifications each keep a `SaleAnnouncement` recording that a
Flight's sale has opened, and both learn it only from Catalog's
`FlightSaleStarted` (ADR-0002, ADR-0003). Neither seeds one.

Catalog instead seeds its Flights with `SaleStartHandledAt` **null**, including
the sales whose windows have already opened or closed, so the announcer makes
those announcements for real on its next scan. This is the case ADR-0003
anticipated in as many words — "a sale seeded straight into the database" — and
the reason a seeded live Flight becomes holdable one scheduler tick after
Catalog starts rather than never.

Writing the window into Ordering directly would have seeded it into believing
something it was never told, and hidden a broken announcement path behind data
that happened to look right. Seeding is the one moment the whole announcement
path is exercised end to end on every fresh clone, which makes it worth more as
a test than as a shortcut.

## Consequences

- **Catalog seeds its own SeatCounts rather than waiting for events.** It always
  had to: a movement event carries a delta and never a total, so the baseline
  comes from seeding either way (`SeatCountsProjector` says so). The demo world
  states exactly which Seats Ordering will seed as Held and Confirmed, so both
  services read the same answer from the same place. The counts' high-water mark
  is set to the newest *seeded* movement, which is in the past — so the first
  real movement a buyer causes is newer, and is folded in rather than dropped as
  a straggler.

- **Seeded Held Seats carry a far longer TTL than a real checkout.** They exist
  to show what Held looks like, and the expiry sweep would otherwise Release them
  partway through the first demo and leave the state unreachable without a
  reseed. `DemoSeed:HeldSeatTtl` is the knob.

- **The watch-demo Flight's sale opens seconds after the seed, once.** Its
  `SaleStartsAt` is stamped when the store is first seeded and never re-armed:
  re-arming would mean rewriting seeded rows on every restart, which is the one
  thing idempotence forbids. A stack that has been up longer than that has
  already had its moment; `docker compose down -v` is what gets it back.

  This is also the one place the services' separate anchors matter. Catalog
  stamps that `SaleStartsAt`; Notifications seeds the Watch meant to fire on it.
  If Notifications' anchor fell more than the lead time behind Catalog's, the
  sale would be announced before the Watch existed and the alert would never
  fire — a demo quietly missing the thing it exists to show, rather than a
  broken store. Both write local SQLite and seed within a second of each other,
  so it would take Notifications' own store being unreachable for most of a
  minute while Catalog's was not; `DemoSeed:WatchDemoLeadTime` is the margin if
  a slow environment ever needs a wider one. Everywhere else the drift costs
  nothing, because no rule compares a timestamp one service wrote against one
  another wrote.

- **Most Seats on the nearly-sold-out Flight belong to buyers with no account.**
  A cabin that full needs some thirty buyers, and dealing them among four demo
  accounts would leave each holding eight separate bookings on one Flight — a
  booking history that reads like a bug. Nobody signs in as these, which is what
  nearly every passenger on a real flight is.

- **Demo buyers are created through `UserManager`, not by inserting rows**, so
  they are hashed and normalised by the code that registers a real buyer. They
  keep their derived id rather than the Version 7 one registration mints: every
  other service has already written Holds, Watches, and Notifications against
  that exact Guid, and none of them could be told a different one.

- **Seeding runs after the migration and before readiness**, inside the same
  retry loop (`DataStoreStartupService`). A service is therefore never served
  while it is migrated but still empty — the seeding twin of the unmigrated
  window that loop already closed — and a seed that fails is retried with the
  same backoff as a database that is not up yet.

- **`DemoData` is content, not plumbing, and the project boundary says so.**
  Delete it and the system still runs; it simply comes up empty, which is what
  it did before this.
