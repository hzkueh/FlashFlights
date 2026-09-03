import { Badge } from '@/components/ui/badge'
import { type BackendState, useBackendStatus } from '@/hooks/use-backend-status'

const LABELS = {
  checking: 'Checking…',
  connected: 'Connected',
  disconnected: 'Disconnected',
} as const

const VARIANTS = {
  checking: 'secondary',
  connected: 'default',
  disconnected: 'destructive',
} as const

const DOTS = {
  checking: 'bg-muted-foreground animate-pulse',
  connected: 'bg-emerald-500',
  disconnected: 'bg-red-500',
} as const

function describe({ status, health }: BackendState): string {
  switch (status) {
    case 'connected':
      return `${health?.service ?? 'backend'}: ${health?.status ?? 'Healthy'}`
    case 'disconnected':
      return 'The backend did not answer through the gateway'
    case 'checking':
      return 'Asking the backend whether it is ready'
  }
}

/** Whether the backend behind the gateway is answering. */
export function BackendStatusBadge() {
  const state = useBackendStatus()

  return (
    <Badge role="status" aria-live="polite" variant={VARIANTS[state.status]} title={describe(state)}>
      <span aria-hidden className={`size-1.5 rounded-full ${DOTS[state.status]}`} />
      {LABELS[state.status]}
    </Badge>
  )
}
