/** Why a file operation was refused, for each host to turn into its own error (an HTTP status, an IPC marker). */
export type FileErrorCode = 'not-found' | 'exists' | 'read-only' | 'into-itself' | 'not-a-file';

/** A refusal the user can act on, as opposed to an unexpected file-system failure. */
export class FileError extends Error {
  readonly code: FileErrorCode;

  constructor(code: FileErrorCode, message: string) {
    super(message);
    this.name = 'FileError';
    this.code = code;
  }
}
