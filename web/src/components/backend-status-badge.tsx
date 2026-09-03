import { Badge } from '@/components/ui/badge'
import { type BackendState, type BackendStatus, useBackendStatus } from '@/hooks/use-backend-status'
import { WATCHED_SERVICE } from '@/lib/gateway'

/**
 * Everything that varies by status, in one place — so a fourth status is one
 * entry rather than an edit to four parallel maps.
 */
const PRESENTATION = {
  checking: {
    label: 'Checking…',
    variant: 'secondary',
    dot: 'bg-muted-foreground animate-pulse',
    tooltip: () => `Asking ${WATCHED_SERVICE} whether it is ready`,
  },
  connected: {
    label: 'Connected',
    variant: 'default',
    dot: 'bg-emerald-500',
    tooltip: (health: BackendHealthOrNull) =>
      `${health?.service ?? WATCHED_SERVICE}: ${health?.status ?? 'Healthy'}`,
  },
  disconnected: {
    label: 'Disconnected',
    variant: 'destructive',
    dot: 'bg-red-500',
    tooltip: () => `${WATCHED_SERVICE} did not answer through the gateway`,
  },
} as const satisfies Record<BackendStatus, Presentation>

type BackendHealthOrNull = BackendState['health']

interface Presentation {
  label: string
  variant: 'secondary' | 'default' | 'destructive'
  dot: string
  tooltip: (health: BackendHealthOrNull) => string
}

/**
 * Whether the one service this SPA watches is answering through the gateway.
 * Named after that service rather than "the backend": the other two services
 * being down would leave this reading Connected.
 */
export function BackendStatusBadge() {
  const { status, health } = useBackendStatus()
  const { label, variant, dot, tooltip } = PRESENTATION[status]

  return (
    <Badge role="status" aria-live="polite" variant={variant} title={tooltip(health)}>
      <span aria-hidden className={`size-1.5 rounded-full ${dot}`} />
      {label}
    </Badge>
  )
}
