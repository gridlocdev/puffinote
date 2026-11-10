/**
 * A mistake the user can fix (missing file, empty query, not a folder): printed as
 * `puffinote: <message>` with exit code 1, never as a stack trace.
 */
export class CliError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'CliError';
  }
}

/**
 * Whether an exception is the user's to fix rather than a bug. File-system failures carry an errno
 * `code` (`ENOENT`, `EACCES`); bad input is a plain `Error` or `SyntaxError`. A `TypeError`, a Node
 * internal error (`ERR_*` codes) and a `RangeError` are bugs and keep their stack trace.
 */
export function isExpectedError(e: unknown): e is Error {
  if (e instanceof CliError) return true;
  if (!(e instanceof Error) || e instanceof TypeError || e instanceof RangeError) return false;
  const code = (e as NodeJS.ErrnoException).code;
  if (code !== undefined) return typeof code === 'string' && ERRNO_CODE.test(code);
  return e.constructor === Error || e instanceof SyntaxError;
}

/** An errno code from a system call (`ENOENT`, `EISDIR`), not a Node `ERR_*` code. */
const ERRNO_CODE = /^E[A-Z0-9]+$/;
