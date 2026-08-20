import {
  Badge, Code, Table, TableTbody, TableTd, TableTh, TableThead, TableTr, Text,
} from '@mantine/core';
import type { AuditEntry } from '@/lib/categories';
import type { MessageKey } from '@/i18n';
import { getT } from '@/i18n/server';

const COLOURS: Record<string, string> = {
  Created: 'green',
  Updated: 'blue',
  Moved: 'grape',
  Deleted: 'red',
  ChildPromoted: 'orange',
  LinkAdded: 'teal',
  LinkRemoved: 'yellow',
  CategoriesAssigned: 'cyan',
  PasswordReset: 'orange',
  PasswordChanged: 'blue',
  RolesChanged: 'grape',
  Disabled: 'red',
  Enabled: 'green',
};

/**
 * Flat `TableThead` imports rather than `Table.Thead`: this renders inside a Server
 * Component, where dot notation on a client component is undefined.
 */
export async function AuditTable({ entries, showEntity = true }: { entries: AuditEntry[]; showEntity?: boolean }) {
  const t = await getT();

  if (entries.length === 0) {
    return <Text c="dimmed">{t('audit.empty')}</Text>;
  }

  return (
    <Table striped highlightOnHover withTableBorder>
      <TableThead>
        <TableTr>
          <TableTh w={170}>{t('audit.when')}</TableTh>
          <TableTh w={140}>{t('audit.who')}</TableTh>
          <TableTh w={140}>{t('audit.action')}</TableTh>
          {showEntity && <TableTh w={220}>{t('audit.record')}</TableTh>}
          <TableTh>{t('audit.whatChanged')}</TableTh>
        </TableTr>
      </TableThead>
      <TableTbody>
        {entries.map((entry) => (
          <TableTr key={entry.id}>
            <TableTd>
              <Text size="sm">{new Date(entry.changedAt).toLocaleString()}</Text>
            </TableTd>
            <TableTd>
              {/* Snapshotted at write time, so it still reads correctly after an account is removed. */}
              <Text size="sm">{entry.changedByName}</Text>
            </TableTd>
            <TableTd>
              <Badge color={COLOURS[entry.action] ?? 'gray'} variant="light">
                {t(`action.${entry.action}` as MessageKey)}
              </Badge>
            </TableTd>
            {showEntity && (
              <TableTd>
                <Text size="sm">{entry.entityName}</Text>
                <Text size="xs" c="dimmed">{entry.entityType}</Text>
              </TableTd>
            )}
            <TableTd>
              {entry.summary ? <Code>{entry.summary}</Code> : <Text c="dimmed" size="sm">—</Text>}
            </TableTd>
          </TableTr>
        ))}
      </TableTbody>
    </Table>
  );
}
