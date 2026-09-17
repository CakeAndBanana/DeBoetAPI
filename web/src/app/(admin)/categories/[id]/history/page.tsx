import { Anchor, Stack, Text, Title } from '@mantine/core';
import { getCategoryHistory } from '@/lib/categories-api';
import { AuditTable } from '@/components/audit-table';
import { AuditPagination } from '@/components/audit-pagination';
import { getT } from '@/i18n/server';

export default async function CategoryHistoryPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ page?: string }>;
}) {
  const t = await getT();
  const { id } = await params;
  const { page } = await searchParams;
  const history = await getCategoryHistory(id, page ? Number(page) : undefined);
  const name = history.items[0]?.entityName ?? 'category';

  return (
    <Stack>
      <div>
        <Title order={2}>{t('history.title', { name })}</Title>
        <Text c="dimmed" size="sm">
          {t('history.kept')}{' '}
          {/* A plain href, not component={Link}: passing a function prop from a Server
              Component to a client component like Anchor is not allowed. */}
          <Anchor href="/categories">{t('history.back')}</Anchor>
        </Text>
      </div>

      <AuditTable entries={history.items} showEntity={false} />

      <AuditPagination
        page={history.page}
        pageSize={history.pageSize}
        totalCount={history.totalCount}
        totalPages={history.totalPages}
        path={`/categories/${id}/history`}
      />
    </Stack>
  );
}
