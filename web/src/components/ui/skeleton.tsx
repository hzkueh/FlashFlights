import { cn } from '@/lib/utils'

/**
 * One grey placeholder block, shaped by its class names to stand in for whatever
 * is still loading. Decorative on its own — a page never renders bare Skeletons,
 * it renders them inside a {@link LoadingPanel}, which is what carries the
 * meaning for anyone not looking at the screen.
 */
export function Skeleton({ className }: { className?: string }) {
  return <div aria-hidden className={cn('animate-pulse rounded-md bg-muted', className)} />
}

/**
 * The frame a page's loading state goes in: the skeleton shapes for a reader
 * looking at the screen, and the sentence they stand for, for a reader who is
 * not.
 *
 * Both halves matter. A skeleton alone is silence to a screen reader, and a line
 * of "Loading…" text alone is a layout that jumps once the content lands — this
 * pairs them so neither page has to remember to.
 */
export function LoadingPanel({
  label,
  className,
  children,
}: {
  /** What is being loaded, as a sentence: "Loading flights…". */
  label: string
  className?: string
  children: React.ReactNode
}) {
  return (
    <div role="status" aria-busy="true" className={className}>
      <span className="sr-only">{label}</span>
      {children}
    </div>
  )
}
