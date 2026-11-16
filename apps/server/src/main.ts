import path from 'node:path';
import { FileApi, parseRoots } from './fileApi.ts';
import { createServer, parseHosts, refuseWithoutToken } from './server.ts';

/**
 * Puffinote NAS server. Configuration is environment-only so it drops straight into Docker:
 *   PUFFINOTE_ROOTS   Name=/dir;Other=/dir2   folders that hold notebooks (required)
 *   PUFFINOTE_STATIC  path to the built web client (default ../web/dist); empty = API only
 *   PUFFINOTE_TOKEN   shared bearer token; required unless the bind address is loopback or
 *                     PUFFINOTE_ALLOW_NO_TOKEN=1 (a reverse proxy in front does the auth)
 *   PUFFINOTE_ALLOW_NO_TOKEN  1 = listen on the network without a token (PRD §3)
 *   PUFFINOTE_HOST    bind address (default 0.0.0.0)
 *   PUFFINOTE_PORT    port (default 8790); plain PORT still works, for older Docker setups
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
const port = Number(env.PUFFINOTE_PORT || env.PORT || 8790);
const host = env.PUFFINOTE_HOST || '0.0.0.0';
const token = env.PUFFINOTE_TOKEN || null;
const refusal = refuseWithoutToken({ host, token, allowNoToken: env.PUFFINOTE_ALLOW_NO_TOKEN === '1' });
if (refusal) {
  console.error(refusal);
  process.exit(1);
}

const server = createServer({
  api: new FileApi(roots),
  staticDir,
  token,
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
  if (!token)
    console.log(
      env.PUFFINOTE_ALLOW_NO_TOKEN === '1'
        ? '  no PUFFINOTE_TOKEN: PUFFINOTE_ALLOW_NO_TOKEN=1 says a reverse proxy in front authenticates'
        : '  no PUFFINOTE_TOKEN: answering this machine only',
    );
});

for (const sig of ['SIGINT', 'SIGTERM'] as const) process.on(sig, () => server.close(() => process.exit(0)));
