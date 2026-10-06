import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// /api is forwarded to the .NET API (http profile in launchSettings.json).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5143' },
  },
})
