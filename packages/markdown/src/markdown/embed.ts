/** The target of a `![[target|size]]` embed, without its size or `#part`. Empty when it names nothing. */
export function embedTarget(meta: string | null | undefined): string {
  return (meta ?? '').split(/[|#]/)[0]!.trim();
}

/** The width an embed asks for (`![[a.png|300]]`, `![[a.png|300x200]]`), or null. */
export function embedWidth(meta: string | null | undefined): number | null {
  const m = /\|\s*(\d+)\s*(?:x\s*\d+\s*)?$/.exec(meta ?? '');
  return m ? Number(m[1]) : null;
}
