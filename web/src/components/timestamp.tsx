'use client';

import { useLocale } from '@/i18n/client';

/**
 * A moment in time, in the reader's own language and timezone.
 *
 * A Client Component because the alternative is formatting on the server, which has neither:
 * it would stamp every row with the container's locale and UTC clock — which is why the
 * history was showing American dates in a Dutch panel, at the wrong time of day.
 *
 * Server and client therefore render different text on purpose, so the hydration warning is
 * suppressed rather than avoided. The `dateTime` attribute carries the unambiguous instant for
 * anything reading the markup.
 */
export function Timestamp({ value }: { value: string }) {
  const locale = useLocale();
  const date = new Date(value);

  if (Number.isNaN(date.valueOf())) {
    return <span>{value}</span>;
  }

  return (
    <time dateTime={value} suppressHydrationWarning style={{ whiteSpace: 'nowrap' }}>
      {new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'medium' }).format(date)}
    </time>
  );
}
