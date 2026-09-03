import { afterEach, describe, expect, it, vi } from 'vitest'

import { BACKEND_READINESS_PATH, fetchBackendHealth } from '@/lib/gateway'

const healthy = { service: 'catalog', status: 'Healthy', checks: {} }

afterEach(() => vi.unstubAllGlobals())

function stubFetch(impl: typeof fetch) {
  vi.stubGlobal('fetch', vi.fn(impl))
}

describe('fetchBackendHealth', () => {
  it('reads the report from a service readiness endpoint behind the gateway', async () => {
    stubFetch(async () => Response.json(healthy))

    await expect(fetchBackendHealth()).resolves.toEqual({ service: 'catalog', status: 'Healthy' })
    expect(fetch).toHaveBeenCalledWith(BACKEND_READINESS_PATH, expect.anything())
  })

  it('rejects when the backend answers unhealthy', async () => {
    stubFetch(async () => Response.json({ service: 'catalog', status: 'Unhealthy' }, { status: 503 }))

    await expect(fetchBackendHealth()).rejects.toThrow(/503/)
  })

  it('rejects when the gateway itself cannot be reached', async () => {
    stubFetch(async () => {
      throw new TypeError('Failed to fetch')
    })

    await expect(fetchBackendHealth()).rejects.toThrow(/Failed to fetch/)
  })
})
