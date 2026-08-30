/**
 * Reading the filename a server picked out of a `Content-Disposition` header.
 *
 * Its own module because it is exactly the kind of thing that fails quietly: get it wrong and
 * the browser still saves a file, just under a name like `download` or `undefined.xlsx`, which
 * nobody notices until they are looking for it a week later.
 */

/**
 * The filename from a `Content-Disposition` header, or `fallback` when there is nothing usable.
 *
 * Prefers RFC 5987's `filename*`, which is what carries a non-ASCII name, and only falls back to
 * the plain `filename` parameter. A name that arrives with a path in it is reduced to its last
 * segment: a server has no business choosing where a file lands, and browsers refuse such names
 * anyway.
 */
export function fileNameFrom(disposition: string | null, fallback: string): string {
  if (!disposition) return fallback;

  const extended = /filename\*\s*=\s*([^']*)'[^']*'([^;]+)/i.exec(disposition);
  if (extended) {
    const decoded = tryDecode(extended[2].trim());
    if (decoded) return sanitise(decoded, fallback);
  }

  const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/i.exec(disposition);
  if (plain) {
    // Group 2 is set only for the quoted form; the bare form needs its trailing spaces trimmed.
    const value = (plain[2] ?? plain[1]).trim();
    if (value) return sanitise(value, fallback);
  }

  return fallback;
}

function tryDecode(value: string): string | null {
  try {
    return decodeURIComponent(value);
  } catch {
    // A stray percent sign is not a reason to fail the download.
    return null;
  }
}

function sanitise(value: string, fallback: string): string {
  const name = value.split(/[/\\]/).pop()?.trim() ?? '';
  return name.length > 0 ? name : fallback;
}
