import {
  Badge, Code, Table, TableTbody, TableTd, TableTh, TableThead, TableTr, Text,
} from '@mantine/core';
import type { AuditEntry } from '@/lib/categories';

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
export function AuditTable({ entries, showEntity = true }: { entries: AuditEntry[]; showEntity?: boolean }) {
  if (entries.length === 0) {
    return <Text c="dimmed">Nothing recorded yet.</Text>;
  }

  return (
    <Table striped highlightOnHover withTableBorder>
      <TableThead>
        <TableTr>
          <TableTh w={170}>When</TableTh>
          <TableTh w={140}>Who</TableTh>
          <TableTh w={140}>Action</TableTh>
          {showEntity && <TableTh w={220}>Record</TableTh>}
          <TableTh>What changed</TableTh>
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
                {entry.action}
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
