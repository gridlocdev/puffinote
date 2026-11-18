/**
 * Drops a failure on purpose. Every call is listed in test/architecture-exceptions.json with its reason, so each
 * deliberately ignored error shows up in review. A failure turned into a value the caller checks
 * (`.catch(() => null)`) isn't ignored and needs none of this.
 */
export function ignoreError(promise: Promise<unknown>, reason: string): Promise<void> {
  void reason;
  return promise.then(
    () => undefined,
    () => undefined,
  );
}

/** `ignoreError` for synchronous work, listed the same way. */
export function ignoreErrorSync(fn: () => unknown, reason: string): void {
  void reason;
  try {
    fn();
  } catch {
    return;
  }
}
