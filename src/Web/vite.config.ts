import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath } from 'node:url'

// https://vite.dev/config/
//
// The API is a separate origin (dev stack: http://localhost:8080, `compose.dev.yml`);
// the SPA calls it same-origin at `/api/v1` (`lib/api/client.ts` API_BASE), so dev
// and preview proxy that prefix to the API. Without it every real-API call 404s (SF4).
// In production Caddy fronts both under one host (`03-api-design.md` §2), so the
// proxy is a dev/e2e-harness concern only.
const apiProxyTarget = process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:8080'

const apiProxy = {
  '/api': {
    target: apiProxyTarget,
    changeOrigin: true,
  },
}

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
