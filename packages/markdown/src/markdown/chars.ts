/** Character classes the tokenizers share, matching .NET's `char.IsWhiteSpace` and `char.IsLetterOrDigit`. */

const whiteSpace = /\s/;
const letterOrDigit = /[\p{L}\p{Nd}]/u;

// The tokenizers call these once per character, so ASCII is decided without a regex.

export function isWhiteSpace(c: string): boolean {
  if (c.length === 1) {
    const code = c.charCodeAt(0);
    if (code < 128) return code === 32 || (code >= 9 && code <= 13);
  }
  return c.length > 0 && whiteSpace.test(c);
}

export function isLetterOrDigit(c: string): boolean {
  if (c.length === 1) {
    const code = c.charCodeAt(0);
    if (code < 128) {
      return (code >= 48 && code <= 57) || (code >= 65 && code <= 90) || (code >= 97 && code <= 122);
    }
  }
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
