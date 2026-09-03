import { Link, useNavigate } from 'react-router'

import { CredentialsForm } from '@/components/credentials-form'
import { useSession } from '@/hooks/use-session'

/**
 * Registering signs the new User straight in — the gateway answers with a token,
 * so making them type the credentials again would be a step with no purpose.
 */
export function RegisterPage() {
  const { signUp } = useSession()
  const navigate = useNavigate()

  return (
    <CredentialsForm
      title="Register"
      submitLabel="Register"
      passwordAutoComplete="new-password"
      onSubmit={async (credentials) => {
        await signUp(credentials)
        await navigate('/', { replace: true })
      }}
      footer={
        <>
          Already registered?{' '}
          <Link to="/login" className="text-foreground underline underline-offset-4">
            Sign in
          </Link>
          .
        </>
      }
    />
  )
}
