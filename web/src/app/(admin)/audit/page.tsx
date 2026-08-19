import { Group, Stack, Text, Title } from '@mantine/core';
import { getAudit } from '@/lib/categories';
import { AuditTable } from '@/components/audit-table';
import { AuditFilter } from './audit-filter';

export default async function AuditPage({
  searchParams,
}: {
  searchParams: Promise<{ entityType?: string; page?: string }>;
}) {
  const { entityType, page } = await searchParams;
  const audit = await getAudit({ entityType, page: page ? Number(page) : undefined });

  return (
    <Stack>
      <Group justify="space-between" align="flex-end">
        <div>
          <Title order={2}>Change history</Title>
          <Text c="dimmed" size="sm">
            {audit.totalCount} recorded change{audit.totalCount === 1 ? '' : 's'}, newest first.
          </Text>
        </div>
        <AuditFilter value={entityType ?? ''} />
      </Group>

      <AuditTable entries={audit.items} />
    </Stack>
  );
}
