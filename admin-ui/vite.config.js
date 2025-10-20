import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
    proxy: {
      '/api': {
        target: 'https://localhost:7000', // tu back corre en HTTPS
        changeOrigin: true,
        secure: false,                    // acepta cert dev
        rewrite: (p) => p.replace(/^\/api/, ''), // <-- quita /api
      },
    },
  },
})