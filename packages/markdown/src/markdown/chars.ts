/** Character classes the tokenizers share, matching .NET's `char.IsWhiteSpace` and `char.IsLetterOrDigit`. */

const whiteSpace = /\s/;
const letterOrDigit = /[\p{L}\p{Nd}]/u;

export function isWhiteSpace(c: string): boolean {
  return c.length > 0 && whiteSpace.test(c);
}

export function isLetterOrDigit(c: string): boolean {
  return c.length > 0 && letterOrDigit.test(c);
}

export function isAsciiDigit(c: string): boolean {
  return c >= '0' && c <= '9' && c.length === 1;
}

export function isAsciiPunctuation(c: string): boolean {
  return (
    c.length === 1 &&
    ((c >= '!' && c <= '/') || (c >= ':' && c <= '@') || (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
  );
}

/** Length of `line` without a trailing CR, so CRLF files tokenize like LF ones. */
export function contentLength(line: string): number {
  return line.length > 0 && line.charCodeAt(line.length - 1) === 13 ? line.length - 1 : line.length;
}
