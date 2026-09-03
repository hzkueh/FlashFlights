import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The SPA is same-origin with the gateway in the compose stack (the gateway
// proxies `/` here), so every call it makes is a root-relative path. In `npm run
// dev` the SPA is on its own origin, and this proxy restores that same shape —
// which is why no component ever needs to know a gateway base URL.
const gateway = process.env.VITE_GATEWAY_ORIGIN ?? 'http://localhost:8080'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  server: {
    proxy: {
      '/api': { target: gateway, changeOrigin: true, ws: true },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
