import { afterEach, describe, expect, it, vi } from 'vitest'

import { BACKEND_READINESS_PATH, fetchBackendHealth } from '@/lib/gateway'
import { reachable, stubFetch, unhealthy, unreachable } from '@/test/backend'

afterEach(() => vi.unstubAllGlobals())

describe('fetchBackendHealth', () => {
  it('reads the report from a service readiness endpoint behind the gateway', async () => {
    stubFetch(reachable)

    await expect(fetchBackendHealth()).resolves.toEqual({ service: 'catalog', status: 'Healthy' })
    expect(fetch).toHaveBeenCalledWith(BACKEND_READINESS_PATH, expect.anything())
  })

  it('rejects when the service answers unhealthy', async () => {
    stubFetch(unhealthy)

    await expect(fetchBackendHealth()).rejects.toThrow(/503/)
  })

  it('rejects when the gateway itself cannot be reached', async () => {
    stubFetch(unreachable)

    await expect(fetchBackendHealth()).rejects.toThrow(/Failed to fetch/)
  })
})
