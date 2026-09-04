import { type FormEvent, type ReactNode, useId, useState } from 'react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { AuthError, type AuthProblem, type Credentials, UNEXPLAINED_FAILURE } from '@/lib/auth'

const NOTHING_WRONG: AuthProblem = { email: [], password: [], general: [] }

interface CredentialsFormProps {
  /** Also the page's heading — these forms are the whole page. */
  title: string
  submitLabel: string
  /** What the browser should offer: an existing password, or a new one to save. */
  passwordAutoComplete: 'current-password' | 'new-password'
  onSubmit: (credentials: Credentials) => Promise<void>
  /** The way to the other form, so neither is a dead end. */
  footer: ReactNode
}

/**
 * Register and sign in differ only in wording and in what they call, so they
 * are one form. It renders the gateway's own grouping of what was wrong —
 * against the email, against the password, or against the attempt — rather than
 * re-deriving that from message text.
 */
export function CredentialsForm({
  title,
  submitLabel,
  passwordAutoComplete,
  onSubmit,
  footer,
}: CredentialsFormProps) {
  const emailId = useId()
  const passwordId = useId()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [problem, setProblem] = useState<AuthProblem>(NOTHING_WRONG)
  const [pending, setPending] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setProblem(NOTHING_WRONG)
    setPending(true)

    try {
      await onSubmit({ email, password })
    } catch (error) {
      setProblem(
        error instanceof AuthError
          ? error.problem
          : { ...NOTHING_WRONG, general: [UNEXPLAINED_FAILURE] },
      )
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="mx-auto w-full max-w-sm space-y-6">
      <h1 className="font-heading text-2xl font-semibold tracking-tight">{title}</h1>

      <form className="space-y-4" onSubmit={handleSubmit} noValidate>
        {problem.general.length > 0 && (
          <div role="alert" className="rounded-lg border border-destructive/50 px-3 py-2 text-sm text-destructive">
            {problem.general.map((message) => (
              <p key={message}>{message}</p>
            ))}
          </div>
        )}

        <div className="space-y-2">
          <Label htmlFor={emailId}>Email</Label>
          <Input
            id={emailId}
            type="email"
            value={email}
            autoComplete="email"
            aria-invalid={problem.email.length > 0}
            aria-describedby={problem.email.length > 0 ? `${emailId}-problem` : undefined}
            onChange={(event) => setEmail(event.target.value)}
          />
          <FieldProblem id={`${emailId}-problem`} messages={problem.email} />
        </div>

        <div className="space-y-2">
          <Label htmlFor={passwordId}>Password</Label>
          <Input
            id={passwordId}
            type="password"
            value={password}
            autoComplete={passwordAutoComplete}
            aria-invalid={problem.password.length > 0}
            aria-describedby={problem.password.length > 0 ? `${passwordId}-problem` : undefined}
            onChange={(event) => setPassword(event.target.value)}
          />
          <FieldProblem id={`${passwordId}-problem`} messages={problem.password} />
        </div>

        <Button type="submit" className="w-full" disabled={pending}>
          {pending ? 'Working…' : submitLabel}
        </Button>
      </form>

      <p className="text-muted-foreground text-sm">{footer}</p>
    </section>
  )
}

function FieldProblem({ id, messages }: { id: string; messages: string[] }) {
  if (messages.length === 0) {
    return null
  }

  return (
    <div id={id} className="space-y-1 text-sm text-destructive">
      {messages.map((message) => (
        <p key={message}>{message}</p>
      ))}
    </div>
  )
}
