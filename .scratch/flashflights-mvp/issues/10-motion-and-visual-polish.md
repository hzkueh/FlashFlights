# 10 — Motion and visual polish

**What to build:** Live changes read as smooth and legible rather than as things blinking in and out. The app looks like a considered product in both themes.

**Blocked by:** 07, 08.

**Status:** ready-for-human

- [x] Framer Motion transitions on the seat map as Seats change status — animated, not instant swaps.
- [x] Countdown treatments for the Hold timer and sale windows, with visible urgency as time runs low.
- [x] Both themes look deliberate; no unstyled or contrast-broken states.
- [x] Loading, empty, and error states exist on every page.
- [x] Responsive down to mobile widths; the seat map stays usable on a small screen.
- [x] Motion respects `prefers-reduced-motion`.

## Notes

Motion is Framer Motion (shipped as the `motion` package), the stack the spec
names. It is used in three places and no more: a seat beats when its status
moves, the list settles in a card at a time, and a countdown breathes once it
turns critical. Everything else — colour, ring, skeleton pulse — is CSS, because
a stylesheet can be silenced wholesale by `prefers-reduced-motion` and an inline
style cannot.

Three seams carry the work, each a pure or near-pure unit the components only
render:

- `lib/urgency.ts` decides *how loud* a countdown reads. Two scales, not one: a
  Hold's TTL is about two minutes and a sale window hours or days, so a single
  threshold would leave every Hold permanently critical or every sale permanently
  calm. `elapsed` is its own level so a dead timer stops pulsing rather than
  shouting forever.
- `hooks/use-recently-changed.ts` answers what a live map cannot say on its own:
  which of these seats just moved. It reports `false` on first render by design —
  on load every seat is new, and flashing the whole cabin would say a hundred
  things changed when nothing had.
- `hooks/use-reduced-motion.ts` is the switch every animated component checks.
  Hand-rolled rather than taken from Framer Motion, whose own hook caches the
  media query in a module-level singleton: the first test file to touch it would
  pin the preference for every file after it.

An upcoming sale's countdown never turns urgent, however close it is to opening.
Nothing is being lost while a buyer waits for a window they cannot buy in yet, so
urgency there would be noise. Only a live window running out earns it.

`prefers-reduced-motion` is honoured twice over: each component drops its own
motion, and a global rule in `index.css` flattens every CSS animation and
transition. The countdown still changes colour and a changed seat still takes its
ring — a reader who asked for less movement still has to see that time is running
out, and which seat went.

On item 4, "every page" means every page that loads something. The two auth
pages have a pending state and an error state but no empty state, which a form
cannot have; `not-found-page` is itself an empty state. The four loading pages —
flights, flight detail, bookings, notifications — have all three.

Two things this ticket changed beyond its own wording, both in service of item 4:

- **The inbox had no loading or error state at all.** `useNotifications` swallowed
  a failed read and reported an empty inbox, so an unreachable gateway and "no
  alerts yet" looked identical. It now carries an `InboxStatus`, and the page
  only shows the error when it has nothing to fall back on — a re-read that fails
  while alerts are on screen leaves them there.
- **Neutral `StatePanel`s are silent.** Giving every empty state `role="status"`
  made them collide with the app shell's own connection badge, which is a real
  live region. An empty list is page content; only a panel that appeared *because
  something happened* (a Hold lapsing) asks for the role.

The seat map scrolls sideways inside its card rather than reflowing at narrow
widths: a cabin's shape is information, and a wide-body map squashed into one
column is no longer a seat map. Verified at 375px with a ten-abreast cabin.

Also fixed a latent cross-suite flake this work exposed. Suites stub
`matchMedia` in `beforeEach` and `vi.unstubAllGlobals()` in `afterEach`, which
*deleted* the property between tests, since jsdom has none — anything rendering
in that gap failed for a reason unrelated to its own suite. `test/setup.ts` now
installs a plain baseline that unstubbing restores to.

## Review fixes

The two-axis review caught three defects worth naming, all now fixed and
covered:

- **Every sale countdown was a live region.** `aria-live` was set from a ternary
  with no absent branch, so each flight card's countdown announced itself every
  second — the exact opposite of what its own comment claimed. Only a Hold's
  timer is announced now, and only once critical.
- **The seat beat never played for the change it exists for.** Becoming Held
  turns a selectable button into inert text, and React rebuilds the DOM node when
  the element type changes; Framer reads a rebuilt node as a mount, which
  `initial: false` suppresses. Available-to-Held therefore animated nothing. The
  animated element is now a wrapper that outlives the swap, and a test asserts
  that node identity survives the change rather than just that the seat is
  marked.
- **Confirmed seats failed WCAG AA.** The new tokens were 2.96:1 light and 3.90:1
  dark, worse than the greyscale they replaced. Retuned to 5.55:1 and 5.50:1;
  every shipped seat and countdown pair now clears 4.5:1, and the ratios are
  recorded beside the tokens.

Also from the review: the five "couldn't load" panels became one
`GatewayErrorPanel`, `LoadingPanel` moved out of `components/ui/` (which holds
shadcn primitives) beside `StatePanel`, the notifications sign-in prompt joined
the same panel treatment as the bookings one, and the lone
`window.location.reload()` retry went — it was the only page offering one and
the only full-document reload in the codebase.

The palette rebrand was flagged as scope creep and kept deliberately: the
ticket's own headline is that the app should look "like a considered product in
both themes", and a default greyscale shadcn palette is the thing that reads as
unconsidered. `FlightNotFound`'s invented line about a flight being "withdrawn"
was removed — withdrawal is not a concept this domain has.

The one thing not exercised end to end in a browser: the Hold countdown's own
urgency treatment, which needs a signed-in hold against a live stack (no runtime
seed data until ticket 11). It renders through the same `Countdown` as the sale
windows, whose critical treatment was checked on screen, and `holdUrgency` is
unit-tested at each threshold.
