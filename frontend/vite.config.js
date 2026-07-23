import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// The scene is meant to live at a dedicated /jarvis route on the ASP.NET Core app
// (Tier 0 decision). `base: '/jarvis/'` keeps built asset URLs correct once the
// backend serves this from wwwroot/jarvis; it's harmless during `npm run dev` too.
export default defineConfig({
  plugins: [vue()],
  base: '/jarvis/',
  server: {
    port: 5173,
    proxy: {
      // Tier 1 endpoints. Adjust the target port to match your `dotnet run` profile
      // (Properties/launchSettings.json - "http" profile is localhost:5158).
      '/api': {
        target: 'http://localhost:5158',
        changeOrigin: true
      },
      // Tier 6 will add the observer socket; proxied here already so it works
      // without more config later.
      '/ws': {
        target: 'ws://localhost:5158',
        ws: true
      }
    }
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true
  }
})