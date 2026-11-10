import preact from '@preact/preset-vite';
import { defineConfig } from 'vite';

// The editor playground: `pnpm --filter @puffinote/ui demo`. Not shipped; the apps come in a later phase.
export default defineConfig({
  root: import.meta.dirname,
  plugins: [preact()],
  server: { port: 5199 },
});
