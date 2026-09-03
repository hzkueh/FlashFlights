/**
 * Every screen in the spec has a route from this ticket onward, so later work
 * replaces a page rather than also having to wire routing. Each one names the
 * issue that fills it in.
 *
 * "Issue", not "ticket": CONTEXT.md rules "Ticket" out of user-visible copy,
 * because in an airline app it reads as the boarding document.
 */
export function PlaceholderPage({ title, issue }: { title: string; issue: string }) {
  return (
    <section className="space-y-2">
      <h1 className="font-heading text-2xl font-semibold tracking-tight">{title}</h1>
      <p className="text-muted-foreground text-sm">Arrives in issue {issue}.</p>
    </section>
  )
}
