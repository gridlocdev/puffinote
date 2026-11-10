import { readFileSync, writeSync } from 'node:fs';

/**
 * Everything the tool touches outside the files it reads: the three standard streams and the app
 * launcher. Production wires the real process; tests capture the streams and stub the launcher, so
 * the whole CLI runs in-process.
 */
export interface CliIo {
  /** Text to stdout. */
  stdout(text: string): void;
  /** Text to stderr. */
  stderr(text: string): void;
  /** All of stdin, as bytes. */
  readStdin(): Uint8Array;
  /** Open a notebook folder in the Puffinote app; returns the exit code. */
  launch(folder: string): number;
}

/** The output side of a run: writes through `CliIo`, line helpers included. */
export class Console {
  readonly io: CliIo;

  constructor(io: CliIo) {
    this.io = io;
  }

  out(text: string): void {
    this.io.stdout(text);
  }

  outLine(text = ''): void {
    this.io.stdout(text + '\n');
  }

  errLine(text = ''): void {
    this.io.stderr(text + '\n');
  }
}

function writeAll(fd: number, data: string): void {
  const bytes = Buffer.from(data, 'utf8');
  let offset = 0;
  while (offset < bytes.length) {
    try {
      offset += writeSync(fd, bytes, offset, bytes.length - offset);
    } catch (e) {
      // A non-blocking pipe can push back; retry until the reader drains it.
      if ((e as NodeJS.ErrnoException).code !== 'EAGAIN') throw e;
    }
  }
}

/** The real process streams. Writes are synchronous so `process.exit` never truncates output. */
export function processIo(launch: (folder: string) => number): CliIo {
  return {
    stdout: (text) => writeAll(1, text),
    stderr: (text) => writeAll(2, text),
    readStdin: () => readFileSync(0),
    launch,
  };
}
