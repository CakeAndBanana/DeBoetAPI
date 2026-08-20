import { Anchor, Stack, Text, Title } from '@mantine/core';
import { getCategoryHistory } from '@/lib/categories';
import { AuditTable } from '@/components/audit-table';
import { getT } from '@/i18n/server';

export default async function CategoryHistoryPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const t = await getT();
  const { id } = await params;
  const history = await getCategoryHistory(id);
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
    </Stack>
  );
}
