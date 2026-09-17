import { Group, Stack, Text, Title } from '@mantine/core';
import { apiFetch } from '@/lib/api';
import type { Paged } from '@/lib/categories';
import { getAudit } from '@/lib/categories-api';
import type { components } from '@/lib/api-types';
import { AuditTable } from '@/components/audit-table';
import { getT } from '@/i18n/server';
import { AuditPagination } from '@/components/audit-pagination';
import { AuditFilter } from './audit-filter';

type ManagedUser = components['schemas']['ManagedUserResponse'];

interface Query {
  entityType?: string;
  action?: string;
  changedById?: string;
  page?: string;
}

export default async function AuditPage({ searchParams }: { searchParams: Promise<Query> }) {
  const { entityType, action, changedById, page } = await searchParams;

  // Both are Admin-only, exactly like this page, so the account list costs nothing extra in
  // permissions. It is what turns the person filter into a picker rather than a uuid to paste.
  const [audit, users] = await Promise.all([
    getAudit({ entityType, action, changedById, page: page ? Number(page) : undefined }),
    apiFetch<Paged<ManagedUser>>('/api/v1/users?pageSize=100'),
  ]);

  const t = await getT();
  const people = users.items.map((u) => ({ value: u.id, label: u.displayName || u.userName }));
  const filtered = !!(entityType || action || changedById);

  // Singular and plural are separate keys: not every language pluralises by adding an "s".
  const countKey = filtered
    ? (audit.totalCount === 1 ? 'audit.countMatchingOne' : 'audit.countMatching')
    : (audit.totalCount === 1 ? 'audit.countRecordedOne' : 'audit.countRecorded');

  return (
    <Stack>
      <Group justify="space-between" align="flex-end">
        <div>
          <Title order={2}>{t('audit.title')}</Title>
          <Text c="dimmed" size="sm">
            {t(countKey, { count: audit.totalCount })}
          </Text>
        </div>
        <AuditFilter
          values={{
            entityType: entityType ?? '',
            action: action ?? '',
            changedById: changedById ?? '',
          }}
          people={people}
        />
      </Group>

      <AuditTable entries={audit.items} />

      <AuditPagination
        page={audit.page}
        pageSize={audit.pageSize}
        totalCount={audit.totalCount}
        totalPages={audit.totalPages}
        path="/audit"
        params={Object.fromEntries(
          Object.entries({ entityType, action, changedById }).filter(([, v]) => !!v),
        ) as Record<string, string>}
      />
    </Stack>
  );
}
