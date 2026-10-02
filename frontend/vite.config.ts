import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => ({
  plugins: [react()],
  define: { 'import.meta.env.VITE_DEMO': JSON.stringify(mode === 'demo' ? 'true' : 'false') },
  server: { proxy: { '/api': 'http://localhost:5080', '/auth': 'http://localhost:5080', '/signin-google': 'http://localhost:5080' } },
  test: { globals: true, environment: 'jsdom', setupFiles: './src/test-setup.ts' },
}));
