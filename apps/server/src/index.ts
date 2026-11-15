// The NAS server as a library: what `main.ts` wires from the environment, for tests and other hosts.
export { ApiError, FileApi, parseRoots, type Entry, type Root } from './fileApi.ts';
export { createServer, type ServerOptions } from './server.ts';
