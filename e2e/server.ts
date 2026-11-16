// Where the scenarios and the driver find the app: the NAS server serving the built client over a fixture
// notebook. Never point it at real notes.
import {
  cpSync,
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  rmSync,
  utimesSync,
  writeFileSync,
} from 'node:fs';
import { fileURLToPath } from 'node:url';

export const ROOT = fileURLToPath(new URL('..', import.meta.url));
export const DATA = `${ROOT}.e2e-data`;
export const PORT = 8797;
export const BASE_URL = `http://127.0.0.1:${PORT}`;
/** The page's clock in every scenario and in the driver, so dates in the app read the same on every run. */
export const NOW = new Date('2027-01-15T09:00:00Z');

export const SERVER_COMMAND = ['apps/server/src/main.ts'];
// One root, so the client opens it as the notebook straight away.
export const SERVER_ENV: Record<string, string> = {
  PUFFINOTE_PORT: String(PORT),
  PUFFINOTE_HOST: '127.0.0.1',
  PUFFINOTE_ROOTS: `E2E=${DATA}`,
  PUFFINOTE_STATIC: `${ROOT}apps/web/dist`,
};

/** Replace .e2e-data with a fresh copy of e2e/fixtures/<fixture>. Never point this at real notes. */
export function seedData(fixture: string): void {
  rmSync(DATA, { recursive: true, force: true });
  mkdirSync(DATA, { recursive: true });
  cpSync(fileURLToPath(new URL(`./fixtures/${fixture}`, import.meta.url)), DATA, { recursive: true });
  // The Library's note list shows each note's date, so the copies get the same one on every run.
  const seeded = new Date(NOW.getTime() - 2 * 24 * 60 * 60 * 1000);
  for (const file of readdirSync(DATA, { recursive: true, encoding: 'utf8' }))
    utimesSync(`${DATA}/${file}`, seeded, seeded);
}

/** A note under .e2e-data as the server has it now, to check the disk and not just the screen. */
export function readData(file: string): string {
  return readFileSync(`${DATA}/${file}`, 'utf8');
}

/** Like readData, but undefined until the app has written the file. */
export function readDataIfThere(file: string): string | undefined {
  return existsSync(`${DATA}/${file}`) ? readData(file) : undefined;
}

/** Change a note on disk, as another app or machine would. */
export function writeData(file: string, text: string): void {
  writeFileSync(`${DATA}/${file}`, text);
}
