import { Link, useLocation, useNavigate } from 'react-router'

import { CredentialsForm } from '@/components/credentials-form'
import { useSession } from '@/hooks/use-session'

/** A returning User signs in and lands back on the flights they came for. */
export function LoginPage() {
  const { signIn } = useSession()
  const navigate = useNavigate()
  const location = useLocation()
  // A visitor sent here from a seat map they tried to hold carries where they
  // came from, so signing in returns them to it rather than to the list.
  const from = (location.state as { from?: string } | null)?.from ?? '/'

  return (
    <CredentialsForm
      title="Sign in"
      submitLabel="Sign in"
      passwordAutoComplete="current-password"
      onSubmit={async (credentials) => {
        await signIn(credentials)
        // Replace, so Back from the flights list does not land on a sign-in
        // form the User has already used.
        await navigate(from, { replace: true })
      }}
      footer={
        <>
          New to FlashFlights?{' '}
          <Link to="/register" className="text-foreground underline underline-offset-4">
            Register
          </Link>
          .
        </>
      }
    />
  )
}
