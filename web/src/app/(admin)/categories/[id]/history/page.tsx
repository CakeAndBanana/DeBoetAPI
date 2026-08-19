import { Anchor, Stack, Text, Title } from '@mantine/core';
import { getCategoryHistory } from '@/lib/categories';
import { AuditTable } from '@/components/audit-table';

export default async function CategoryHistoryPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  const history = await getCategoryHistory(id);
  const name = history.items[0]?.entityName ?? 'category';

  return (
    <Stack>
      <div>
        <Title order={2}>History — {name}</Title>
        <Text c="dimmed" size="sm">
          Kept even if the category is deleted.{' '}
          {/* A plain href, not component={Link}: passing a function prop from a Server
              Component to a client component like Anchor is not allowed. */}
          <Anchor href="/categories">Back to categories</Anchor>
        </Text>
      </div>

      <AuditTable entries={history.items} showEntity={false} />
    </Stack>
  );
}
