import { spawn } from 'node:child_process';
import { CliError } from './errors.ts';

/**
 * Open a notebook folder in the Puffinote desktop app. On macOS `open -a` reaches the installed
 * bundle, running or not; the folder travels as an Apple Event, which the app handles (open-file).
 */
export function launchApp(folder: string): number {
  if (process.platform !== 'darwin') {
    throw new CliError(
      'Opening a notebook in the app from the command line is only supported on macOS; open it from the desktop app.',
    );
  }
  try {
    const child = spawn('open', ['-a', 'Puffinote', folder], { stdio: 'ignore', detached: true });
    child.on('error', () => undefined);
    child.unref();
  } catch (e) {
    throw new CliError(`Couldn't launch Puffinote: ${(e as Error).message}`);
  }
  return 0;
}
