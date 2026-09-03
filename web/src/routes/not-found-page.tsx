import { Link } from 'react-router'

import { Button } from '@/components/ui/button'

export function NotFoundPage() {
  return (
    <section className="space-y-4">
      <h1 className="font-heading text-2xl font-semibold tracking-tight">Not found</h1>
      <p className="text-muted-foreground text-sm">That page isn&apos;t part of FlashFlights.</p>
      <Button asChild variant="outline">
        <Link to="/">Back to flights</Link>
      </Button>
    </section>
  )
}
