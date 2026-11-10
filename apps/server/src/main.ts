import path from 'node:path';
import { FileApi, parseRoots } from './fileApi.ts';
import { createServer, parseHosts } from './server.ts';

/**
 * Puffinote NAS server. Configuration is environment-only so it drops straight into Docker:
 *   PUFFINOTE_ROOTS   Name=/dir;Other=/dir2   folders that hold notebooks (required)
 *   PUFFINOTE_STATIC  path to the built web client (default ../web/dist); empty = API only
 *   PUFFINOTE_TOKEN   optional shared bearer token; leave unset when a reverse proxy does auth
 *   PUFFINOTE_HOST    bind address (default 0.0.0.0)    PORT  (default 8790)
 *   PUFFINOTE_HOSTS   extra Host names (comma-separated) served when there is no token; IP
 *                     literals and localhost are always accepted, anything else gets 421
 *   PUFFINOTE_MAX_MB  largest upload in MiB (default 64)
 */
const env = process.env;
const here = path.dirname(new URL(import.meta.url).pathname);
const roots = parseRoots(env.PUFFINOTE_ROOTS ?? '', process.cwd());
if (roots.length === 0) {
  console.error('Set PUFFINOTE_ROOTS, e.g. PUFFINOTE_ROOTS="Notes=/data/notes"');
  process.exit(1);
}
const staticDir =
  env.PUFFINOTE_STATIC === ''
    ? null
    : path.resolve(env.PUFFINOTE_STATIC ?? path.join(here, '../../web/dist'));
const port = Number(env.PORT ?? 8790);
const host = env.PUFFINOTE_HOST ?? '0.0.0.0';

const server = createServer({
  api: new FileApi(roots),
  staticDir,
  token: env.PUFFINOTE_TOKEN || null,
  maxBytes: maxMb(env.PUFFINOTE_MAX_MB) * 1024 * 1024,
  allowedHosts: parseHosts(env.PUFFINOTE_HOSTS),
});

function maxMb(spec: string | undefined): number {
  const n = Number(spec);
  return spec && Number.isFinite(n) && n > 0 ? n : 64;
}

server.listen(port, host, () => {
  console.log(`Puffinote listening on http://${host}:${port}`);
  for (const r of roots) console.log(`  root '${r.name}' -> ${r.dir}`);
  if (!env.PUFFINOTE_TOKEN)
    console.log('  no PUFFINOTE_TOKEN set: keep this on your LAN or behind the NAS reverse proxy');
});

for (const sig of ['SIGINT', 'SIGTERM'] as const) process.on(sig, () => server.close(() => process.exit(0)));
