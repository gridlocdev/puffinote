import { readFileSync } from 'node:fs';
import { runCli } from './cli.ts';
import { processIo } from './infra/io.ts';
import { launchApp } from './infra/launcher.ts';

/** The `puffinote` executable: the real process streams and the app launcher. */

const pkg = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8')) as {
  version: string;
};
process.exitCode = runCli(process.argv.slice(2), processIo(launchApp), pkg.version);
