import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The same /api/... paths as nginx in the container: the prefix is stripped before the request reaches the service.
export default defineConfig({
  plugins: [react()],
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
