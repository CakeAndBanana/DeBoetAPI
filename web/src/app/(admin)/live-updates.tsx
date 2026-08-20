'use client';

import { useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';

/** Long enough to fold a burst of changes into one refresh, short enough to feel immediate. */
const SETTLE_MS = 300;

/**
 * Keeps every open panel in step with the others.
 *
 * Without this, editing on a phone leaves a browser showing yesterday's tree until someone
 * reloads it — and two people can overwrite each other while both believe they are current.
 *
 * `router.refresh()` re-runs the Server Components for whatever page is open and swaps in the
 * new markup, so this works for the tree, the user list and the history alike without any of
 * them knowing it exists. Client state — a half-typed form, the search box, which branches are
 * collapsed — is preserved across it.
 */
export function LiveUpdates() {
  const router = useRouter();
  const settle = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(() => {
    // EventSource reconnects by itself on a dropped connection, which is most of why this is
    // SSE and not a socket.
    const source = new EventSource('/api/events');

    const refresh = () => {
      clearTimeout(settle.current);
      settle.current = setTimeout(() => router.refresh(), SETTLE_MS);
    };

    source.addEventListener('change', refresh);

    return () => {
      clearTimeout(settle.current);
      source.close();
    };
  }, [router]);

  return null;
}
