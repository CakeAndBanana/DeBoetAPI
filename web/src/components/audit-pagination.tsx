'use client';

import { useRouter } from 'next/navigation';
import { Group, Pagination, Text } from '@mantine/core';
import { useT } from '@/i18n/client';

/**
 * Moves between pages of history.
 *
 * The filters travel in the query string, so they are passed in and rebuilt here rather than
 * read from the URL: changing page must not quietly drop the filter you are looking through.
 *
 * Renders nothing at all when everything fits on one page, which is the ordinary case.
 */
export function AuditPagination({
  page, pageSize, totalCount, totalPages, path, params = {},
}: {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  /** Where to navigate, without a query string. */
  path: string;
  /** Anything that must survive a page change — the audit filters. */
  params?: Record<string, string>;
}) {
  const router = useRouter();
  const t = useT();

  if (totalPages <= 1) return null;

  const first = (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, totalCount);

  function go(next: number) {
    const query = new URLSearchParams(params);
    // Page one is the default, so it stays out of the URL and the address is the tidy one.
    if (next > 1) query.set('page', String(next));

    const search = query.toString();
    router.push(search ? `${path}?${search}` : path);
  }

  return (
    <Group justify="space-between" align="center">
      <Text size="sm" c="dimmed">
        {t('audit.showing', { first, last, total: totalCount })}
      </Text>
      <Pagination value={page} total={totalPages} onChange={go} withEdges size="sm" />
    </Group>
  );
}
