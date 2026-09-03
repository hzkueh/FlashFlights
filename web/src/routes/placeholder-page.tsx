/**
 * Every screen in the spec has a route from this ticket onward, so later
 * tickets replace a page rather than also having to wire routing. Each one says
 * which ticket fills it in.
 */
export function PlaceholderPage({ title, ticket }: { title: string; ticket: string }) {
  return (
    <section className="space-y-2">
      <h1 className="font-heading text-2xl font-semibold tracking-tight">{title}</h1>
      <p className="text-muted-foreground text-sm">Arrives in ticket {ticket}.</p>
    </section>
  )
}
