/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

// Identifica este build: o app compara com /version.json para avisar que há uma versão nova no ar.
const buildId = process.env.BUILD_ID ?? Date.now().toString(36)

const api = process.env.VITE_API_PROXY ?? 'http://localhost:5080'

export default defineConfig({
  define: { __BUILD_ID__: JSON.stringify(buildId) },
  plugins: [
    vue(),
    tailwindcss(),
    {
      name: 'emit-version',
      generateBundle() {
        this.emitFile({ type: 'asset', fileName: 'version.json', source: JSON.stringify({ build: buildId }) })
      },
    },
  ],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    // Mesma origem também em desenvolvimento, para o cookie de sessão se comportar como em produção.
    proxy: {
      '/api': { target: api, changeOrigin: false },
    },
  },
  test: {
    environment: 'jsdom',
  },
})
