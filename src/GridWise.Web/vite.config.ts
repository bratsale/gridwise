import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The same /api/... paths as nginx in the container: the prefix is stripped before the request reaches the service.
export default defineConfig({
  plugins: [react()],
  // Two pages: the app, and the small page the hidden iframe of a silent sign-in loads (see src/silent-renew.ts).
  build: {
    rollupOptions: {
      input: { main: 'index.html', silentRenew: 'silent-renew.html' },
    },
  },
  server: {
    proxy: {
      '/api/catalog': {
        target: 'http://localhost:5101',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api\/catalog/, ''),
      },
      '/api/recommendation': {
        target: 'http://localhost:5102',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api\/recommendation/, ''),
      },
      '/api/batch': {
        target: 'http://localhost:5103',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api\/batch/, ''),
      },
    },
  },
})
