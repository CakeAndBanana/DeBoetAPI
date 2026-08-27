import {
  Badge, Code, Table, TableScrollContainer, TableTbody, TableTd, TableTh, TableThead, TableTr,
  Text,
} from '@mantine/core';
import { Timestamp } from './timestamp';
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

  // The short columns size to their content instead of to a fixed width. Pinning them was
  // what broke this table once it was translated: a date wrapped onto three lines, tripling
  // every row's height, and the action badge was clipped to its first letter.
  const tight = { whiteSpace: 'nowrap' as const, width: '1%' };

  return (
    // Scrolls the table rather than the page when it will not fit — on a phone it never does.
    <TableScrollContainer minWidth={720}>
      <Table striped highlightOnHover withTableBorder>
        <TableThead>
          <TableTr>
            <TableTh style={tight}>{t('audit.when')}</TableTh>
            <TableTh style={tight}>{t('audit.who')}</TableTh>
            <TableTh style={tight}>{t('audit.action')}</TableTh>
            {showEntity && <TableTh style={tight}>{t('audit.record')}</TableTh>}
            <TableTh>{t('audit.whatChanged')}</TableTh>
          </TableTr>
        </TableThead>
        <TableTbody>
          {entries.map((entry) => (
            <TableTr key={entry.id}>
              <TableTd style={tight}>
                <Text size="sm" component="span">
                  <Timestamp value={entry.changedAt} />
                </Text>
              </TableTd>
              <TableTd style={tight}>
                {/* Snapshotted at write time, so it still reads correctly after an account is removed. */}
                <Text size="sm">{entry.changedByName}</Text>
              </TableTd>
              <TableTd style={tight}>
                {/*
                  Mantine clips a Badge from both ends: `overflow: hidden` on the root *and* on
                  the label. Either one alone makes the badge's minimum content width zero, so a
                  shrink-to-fit column is free to collapse it to a single letter and an ellipsis
                  — which is exactly what it did. Both have to be turned off for the badge to
                  claim the width of its own text, and only then does the column size to it.
                */}
                <Badge
                  color={COLOURS[entry.action] ?? 'gray'}
                  variant="light"
                  styles={{
                    root: { overflow: 'visible', textOverflow: 'clip' },
                    label: { overflow: 'visible', textOverflow: 'clip' },
                  }}
                >
                  {t(`action.${entry.action}` as MessageKey)}
                </Badge>
              </TableTd>
              {showEntity && (
                <TableTd style={tight}>
                  <Text size="sm">{entry.entityName}</Text>
                  <Text size="xs" c="dimmed">{entry.entityType}</Text>
                </TableTd>
              )}
              {/* Takes whatever is left, and wraps: a summary can run to a couple of lines. */}
              <TableTd>
                {entry.summary
                  ? <Code style={{ whiteSpace: 'normal' }}>{entry.summary}</Code>
                  : <Text c="dimmed" size="sm">—</Text>}
              </TableTd>
            </TableTr>
          ))}
        </TableTbody>
      </Table>
    </TableScrollContainer>
  );
}
