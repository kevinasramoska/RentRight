import { readFileSync } from 'node:fs';
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// Explicit local certificate paths; never export private keys into the repository.
const cert = process.env.RENTRIGHT_DEV_CERT;
const key = process.env.RENTRIGHT_DEV_KEY;
export default defineConfig(({ command, mode }) => {
  if (command === 'serve' && mode !== 'test' && (!cert || !key)) {
    throw new Error('Set RENTRIGHT_DEV_CERT and RENTRIGHT_DEV_KEY to local HTTPS certificate paths. See README.md.');
  }
  return {
    plugins: [react()],
    base: '/app/',
    server: {
      host: '127.0.0.1', port: 5173, strictPort: true,
      https: cert && key ? { cert: readFileSync(cert), key: readFileSync(key) } : undefined,
      proxy: Object.fromEntries(['/api', '/Identity', '/Home', '/Dashboard', '/Properties', '/Tenancies', '/MaintenanceRequest', '/MaintenanceQueue', '/Admin', '/lib', '/css', '/js', '/favicon.ico', '/RentRight.styles.css', '^/$'].map(path => [path, {
        target: 'https://localhost:7216', secure: true, changeOrigin: false,
      }])),
    },
    build: { outDir: '../wwwroot/app', emptyOutDir: true },
    test: { environment: 'jsdom', clearMocks: true },
  };
});
