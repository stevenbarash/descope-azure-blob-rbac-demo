import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 4000,
    proxy: {
      '/api': {
        target: 'https://descope-blob-api.azurewebsites.net',
        changeOrigin: true,
      },
    },
  },
})
