import { timingSafeEqual } from 'node:crypto';
import { readFile, stat } from 'node:fs/promises';
import http from 'node:http';
import { isIP } from 'node:net';
import path from 'node:path';
import { ApiError, type FileApi } from './fileApi.ts';

export interface ServerOptions {
  readonly api: FileApi;
  /** The built web client (`apps/web/dist`); null serves the API only (dev, behind Vite). */
  readonly staticDir: string | null;
  /** When set, every /api request needs `Authorization: Bearer <token>`. */
  readonly token: string | null;
  /** Largest accepted upload. */
  readonly maxBytes: number;
  /**
   * Extra `Host` names accepted when there is no token (`PUFFINOTE_HOSTS`). Without a token only IP literals,
   * `localhost` and these names are served, which stops DNS rebinding.
   */
  readonly allowedHosts?: readonly string[];
}

/** Parse `PUFFINOTE_HOSTS` (`nas.lan, notes.example.com`) into lowercase host names. */
export function parseHosts(spec: string | undefined): string[] {
  return (spec ?? '')
    .split(',')
    .map((h) => h.trim().toLowerCase().replace(/:\d+$/, ''))
    .filter(Boolean);
}

/** Is this `Host` header one we answer without a token? */
export function hostAllowed(hostHeader: string | undefined, allowed: readonly string[]): boolean {
  if (!hostHeader) return false;
  let hostname: string;
  try {
    hostname = new URL(`http://${hostHeader}`).hostname.toLowerCase();
  } catch {
    return false;
  }
  const bare = hostname.replace(/^\[|\]$/g, '');
  if (isIP(bare) !== 0) return true;
  if (bare === 'localhost' || bare.endsWith('.localhost')) return true;
  return allowed.includes(bare);
}

const MIME: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.avif': 'image/avif',
  '.ico': 'image/x-icon',
  '.json': 'application/json',
  '.webmanifest': 'application/manifest+json',
  '.map': 'application/json',
  '.woff2': 'font/woff2',
};

/** Picture types a note's `![](…)` may load straight from /api/file. Anything else is served as bytes. */
const IMAGE = new Set(['.svg', '.png', '.jpg', '.jpeg', '.gif', '.webp', '.avif', '.ico']);

const SECURITY_HEADERS = {
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'no-referrer',
  'X-Frame-Options': 'DENY',
};

/** A notebook file opened on its own (an SVG, say) runs nothing and reaches nothing. */
const FILE_CSP = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; sandbox";

export function createServer(opts: ServerOptions): http.Server {
  return http.createServer((req, res) => {
    handle(req, res, opts).catch((e: unknown) => {
      const status = e instanceof ApiError ? e.status : 500;
      if (status === 500) console.error(e);
      if (!res.headersSent)
        send(res, status, { error: e instanceof Error && status !== 500 ? e.message : 'Server error' });
      else res.end();
    });
  });
}

async function handle(
  req: http.IncomingMessage,
  res: http.ServerResponse,
  opts: ServerOptions,
): Promise<void> {
  if (opts.token === null && !hostAllowed(req.headers.host, opts.allowedHosts ?? []))
    throw new ApiError(421, 'Unknown host: add it to PUFFINOTE_HOSTS or set PUFFINOTE_TOKEN');
  const url = new URL(req.url ?? '/', 'http://localhost');
  if (url.pathname.startsWith('/api/')) {
    // /api/info stays public: it is how the client learns that a token is needed.
    const isInfo = req.method === 'GET' && url.pathname === '/api/info';
    if (!isInfo && !authorized(req, opts.token)) throw new ApiError(401, 'Unauthorized');
    return api(req, res, url, opts);
  }
  if (req.method !== 'GET' && req.method !== 'HEAD') throw new ApiError(405, 'Method not allowed');
  if (!opts.staticDir) throw new ApiError(404, 'Not found');
  return serveStatic(res, opts.staticDir, url.pathname, req.method === 'HEAD');
}

async function api(
  req: http.IncomingMessage,
  res: http.ServerResponse,
  url: URL,
  opts: ServerOptions,
): Promise<void> {
  const q = url.searchParams;
  const root = q.get('root');
  switch (`${req.method} ${url.pathname}`) {
    case 'GET /api/info':
      return send(res, 200, { name: 'Puffinote', auth: opts.token !== null });
    case 'GET /api/roots':
      return send(
        res,
        200,
        opts.api.roots.map((r) => ({ id: r.id, name: r.name })),
      );
    case 'GET /api/list':
      return send(res, 200, await opts.api.list(root, q.get('path')));
    case 'GET /api/file': {
      const rel = q.get('path');
      const { bytes, etag, modified } = await opts.api.read(root, rel);
      if (req.headers['if-none-match'] === etag) {
        res.writeHead(304, { ETag: etag, ...SECURITY_HEADERS });
        return void res.end();
      }
      const ext = path.extname(rel ?? '').toLowerCase();
      res.writeHead(200, {
        'Content-Type': IMAGE.has(ext)
          ? (MIME[ext] ?? 'application/octet-stream')
          : 'application/octet-stream',
        'Content-Length': bytes.length,
        'Cache-Control': 'no-cache',
        'Last-Modified': new Date(modified).toUTCString(),
        'Content-Security-Policy': FILE_CSP,
        ETag: etag,
        ...SECURITY_HEADERS,
      });
      return void res.end(bytes);
    }
    case 'PUT /api/file': {
      const body = await readBody(req, opts.maxBytes);
      const { etag } = await opts.api.write(root, q.get('path'), body, {
        ifMatch: header(req, 'if-match'),
        ifNoneMatch: header(req, 'if-none-match'),
      });
      res.setHeader('ETag', etag);
      return send(res, 200, { etag });
    }
    case 'DELETE /api/file':
      await opts.api.remove(root, q.get('path'));
      return send(res, 200, { ok: true });
    case 'POST /api/mkdir':
      await opts.api.mkdir(root, q.get('path'));
      return send(res, 200, { ok: true });
    case 'POST /api/move':
      await opts.api.move(root, q.get('from'), q.get('to'));
      return send(res, 200, { ok: true });
    default:
      throw new ApiError(404, 'Not found');
  }
}

async function serveStatic(
  res: http.ServerResponse,
  root: string,
  pathname: string,
  head: boolean,
): Promise<void> {
  let decoded: string;
  try {
    decoded = decodeURIComponent(pathname);
  } catch {
    throw new ApiError(400, 'Bad path');
  }
  const rel = decoded.replace(/^\/+/, '');
  let file = path.resolve(root, rel || 'index.html');
  if (!file.startsWith(path.resolve(root) + path.sep) && file !== path.resolve(root))
    throw new ApiError(403, 'Forbidden');
  let s = await stat(file).catch(() => null);
  if (!s || s.isDirectory()) {
    // Single-page app: unknown routes get the shell.
    file = path.join(root, 'index.html');
    s = await stat(file).catch(() => null);
    if (!s) throw new ApiError(404, 'Web client not built — run `pnpm build`');
  }
  const ext = path.extname(file);
  // Only the build's own hashed bundles under <root>/assets/ are immutable.
  const hashed = path.relative(path.resolve(root), file).split(path.sep)[0] === 'assets';
  res.writeHead(200, {
    'Content-Type': MIME[ext] ?? 'application/octet-stream',
    'Content-Length': s.size,
    'Cache-Control': hashed ? 'public, max-age=31536000, immutable' : 'no-cache',
    ...SECURITY_HEADERS,
  });
  if (head) return void res.end();
  res.end(await readFile(file));
}

function authorized(req: http.IncomingMessage, token: string | null): boolean {
  if (token === null) return true;
  const got = req.headers.authorization?.replace(/^Bearer\s+/i, '') ?? '';
  const a = Buffer.from(got);
  const b = Buffer.from(token);
  return a.length === b.length && timingSafeEqual(a, b);
}

function header(req: http.IncomingMessage, name: string): string | null {
  const v = req.headers[name];
  return typeof v === 'string' ? v : null;
}

function readBody(req: http.IncomingMessage, max: number): Promise<Buffer> {
  const declared = Number(req.headers['content-length'] ?? NaN);
  if (declared > max) {
    req.resume(); // drain so the 413 reaches the client
    return Promise.reject(new ApiError(413, 'File too large'));
  }
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    let size = 0;
    let over = false;
    req.on('data', (c: Buffer) => {
      if (over) return;
      size += c.length;
      if (size > max) {
        over = true;
        chunks.length = 0;
        reject(new ApiError(413, 'File too large'));
      } else chunks.push(c);
    });
    req.on('end', () => {
      if (!over) resolve(Buffer.concat(chunks));
    });
    req.on('error', reject);
  });
}

function send(res: http.ServerResponse, status: number, body: unknown): void {
  const json = JSON.stringify(body);
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(json),
    'Cache-Control': 'no-store',
    ...SECURITY_HEADERS,
  });
  res.end(json);
}
