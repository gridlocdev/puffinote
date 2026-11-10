import { spawnSync } from 'node:child_process';
import { CliError } from './errors.ts';

/**
 * Open a notebook folder in the Puffinote desktop app. On macOS `open -a` reaches the installed
 * bundle, running or not; the folder travels as an Apple Event, which the app handles (open-file).
 * `open` returns as soon as Launch Services has the request, so waiting for it is quick, and its exit
 * status says whether the app was found.
 */
export function launchApp(folder: string): number {
  if (process.platform !== 'darwin') {
    throw new CliError(
      'Opening a notebook in the app from the command line is only supported on macOS; open it from the desktop app.',
    );
  }
  const result = spawnSync('open', ['-a', 'Puffinote', folder], { encoding: 'utf8', timeout: 30_000 });
  if (result.error) throw new CliError(`Couldn't launch Puffinote: ${result.error.message}`);
  if (result.status !== 0) {
    const why = result.stderr.trim() || `open exited with status ${String(result.status)}`;
    throw new CliError(`Couldn't launch Puffinote: ${why}`);
  }
  return 0;
}
