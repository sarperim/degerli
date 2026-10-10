import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath } from 'node:url'

// The SPA is same-origin with the API in production (Caddy fronts both under one
// host; `03-api-design.md` §2). In dev and the Playwright e2e harness the SPA is
// served by Vite, so a proxy keeps `/api/v1` same-origin there too — no CORS.
const apiProxyTarget =
  process.env.VITE_API_PROXY_TARGET ?? 'http://127.0.0.1:8080'
const apiProxy = {
  '/api': { target: apiProxyTarget, changeOrigin: true },
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: apiProxy,
  },
  preview: {
    port: 5173,
    proxy: apiProxy,
  },
})
