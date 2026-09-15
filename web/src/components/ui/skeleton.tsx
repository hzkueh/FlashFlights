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
