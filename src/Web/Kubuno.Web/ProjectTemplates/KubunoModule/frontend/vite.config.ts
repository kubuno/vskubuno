import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

/**
 * The module's frontend as one ESM bundle: `npm run build` -> dist/{entry.js, entry.css, chunks/*}.
 *
 * Every specifier the host provides (react, zustand, i18next, @ui, @kubuno/sdk...) stays `external`: at run time
 * the host's import map resolves them to its single instances (bundling a second React would break hooks).
 * `entry.js` exports `register()` and `sdkVersion`; the host calls `register()` after importing it.
 */
const SHARED = new Set([
  'react', 'react-dom', 'react-dom/client',
  'react/jsx-runtime', 'react/jsx-dev-runtime',
  'react-router-dom', '@tanstack/react-query',
  'zustand', 'react-i18next', 'i18next',
  '@ui', '@kubuno/sdk', '@kubuno/drive',
  '@radix-ui/react-dropdown-menu',
])
const isExternal = (s: string) =>
  SHARED.has(s) || s.startsWith('@ui/') || s.startsWith('@kubuno/sdk/') || s.startsWith('@kubuno/drive/')

export default defineConfig({
  base: './',
  plugins: [react(), tailwindcss()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    cssCodeSplit: false,
    rollupOptions: {
      input: fileURLToPath(new URL('./src/entry.ts', import.meta.url)),
      external: isExternal,
      preserveEntrySignatures: 'strict',
      output: {
        format: 'es',
        entryFileNames: 'entry.js',
        chunkFileNames: 'chunks/[name]-[hash].js',
        assetFileNames: (info: { name?: string }) =>
          info.name?.endsWith('.css') ? 'entry.css' : 'assets/[name][extname]',
      },
    },
  },
})
