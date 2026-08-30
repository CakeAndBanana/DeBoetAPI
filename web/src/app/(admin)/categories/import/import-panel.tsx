'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  Alert, Anchor, Badge, Button, Card, Code, FileInput, Group, List, Stack, Table, Text, Title,
} from '@mantine/core';
import { IconArrowLeft, IconCheck, IconFileSpreadsheet, IconUpload } from '@tabler/icons-react';
import { useT } from '@/i18n/client';

/** Mirrors ImportRowResponse on the API. */
interface Row {
  sheet: string;
  row: number;
  id: string;
  name: string;
  state: 'Changed' | 'Stale' | 'Refused' | 'Blocked';
  changes: string[];
  reason: string | null;
}

/** Mirrors ImportResponse on the API. */
interface Preview {
  changed: number;
  unchanged: number;
  stale: number;
  refused: number;
  blocked: number;
  applied: number;
  rows: Row[];
}

type Phase = 'idle' | 'checking' | 'ready' | 'applying' | 'done';

export function ImportPanel() {
  const t = useT();
  const router = useRouter();

  const [file, setFile] = useState<File | null>(null);
  const [phase, setPhase] = useState<Phase>('idle');
  const [preview, setPreview] = useState<Preview | null>(null);
  const [applied, setApplied] = useState(0);
  const [error, setError] = useState<string | null>(null);

  async function send(chosen: File, apply: boolean): Promise<Preview | null> {
    const body = new FormData();
    body.append('file', chosen);

    const response = await fetch(`/api/categories/import${apply ? '?apply=true' : ''}`, {
      method: 'POST',
      body,
    });

    if (response.ok) {
      return (await response.json()) as Preview;
    }

    // The API's own words about what is wrong with the file are far more use than anything
    // this page could say instead.
    const problem = await response.json().catch(() => null);
    setError(problem?.detail ?? t('import.failed'));
    return null;
  }

  async function choose(chosen: File | null) {
    setFile(chosen);
    setPreview(null);
    setError(null);
    setApplied(0);

    if (!chosen) {
      setPhase('idle');
      return;
    }

    setPhase('checking');
    const result = await send(chosen, false);
    setPreview(result);
    setPhase(result ? 'ready' : 'idle');
  }

  async function apply() {
    if (!file) return;

    setPhase('applying');
    setError(null);

    const result = await send(file, true);

    if (!result) {
      setPhase('ready');
      return;
    }

    setApplied(result.applied);
    setPreview(result);
    setPhase('done');

    // The tree behind this page is a Server Component and would otherwise still show the old
    // names until the next navigation.
    router.refresh();
  }

  const busy = phase === 'checking' || phase === 'applying';
  const changed = preview?.changed ?? 0;

  return (
    <Stack>
      <Group justify="space-between" align="flex-start">
        <Title order={2}>{t('import.title')}</Title>
        <Button component={Link} href="/categories" variant="subtle" leftSection={<IconArrowLeft size={16} />}>
          {t('import.back')}
        </Button>
      </Group>

      <Text size="sm" c="dimmed" maw={720}>{t('import.intro')}</Text>

      <Card withBorder maw={720}>
        <Stack>
          <FileInput
            label={t('import.pick')}
            placeholder="categories.xlsx"
            accept=".xlsx"
            value={file}
            onChange={choose}
            disabled={busy}
            leftSection={<IconFileSpreadsheet size={16} />}
            clearable
          />

          {phase === 'checking' && <Text size="sm" c="dimmed">{t('import.checking')}</Text>}

          {error && <Alert color="red" title={t('common.refused')}>{error}</Alert>}

          {phase === 'done' && (
            <Alert color="green" icon={<IconCheck size={16} />}>
              {applied === 1 ? t('import.doneOne') : t('import.done', { count: applied })}
            </Alert>
          )}

          {preview && phase !== 'done' && (
            <>
              <Text size="sm">
                {t('import.summary', { changed, unchanged: preview.unchanged })}
              </Text>

              {changed === 0 && preview.rows.length === 0 && (
                <Text size="sm" c="dimmed">{t('import.nothing')}</Text>
              )}

              <Group>
                <Button
                  onClick={apply}
                  loading={phase === 'applying'}
                  disabled={changed === 0}
                  leftSection={<IconUpload size={16} />}
                >
                  {changed === 1 ? t('import.applyOne') : t('import.apply', { count: changed })}
                </Button>
              </Group>
            </>
          )}
        </Stack>
      </Card>

      {preview && (
        <Stack gap="lg" maw={960}>
          <Section rows={preview.rows} state="Changed" colour="blue" label={t('import.stateChanged')} />
          <Section rows={preview.rows} state="Stale" colour="yellow" label={t('import.stateStale')} help={t('import.staleHelp')} />
          <Section rows={preview.rows} state="Blocked" colour="orange" label={t('import.stateBlocked')} help={t('import.blockedHelp')} />
          <Section rows={preview.rows} state="Refused" colour="red" label={t('import.stateRefused')} help={t('import.refusedHelp')} />
        </Stack>
      )}

      {phase === 'done' && (
        <Anchor component={Link} href="/categories">{t('import.back')}</Anchor>
      )}
    </Stack>
  );
}

/**
 * One state's worth of rows. Rendered only when there are some, so a clean import shows a single
 * table rather than three empty headings.
 */
function Section({
  rows, state, colour, label, help,
}: {
  rows: Row[];
  state: Row['state'];
  colour: string;
  label: string;
  help?: string;
}) {
  const t = useT();
  const mine = rows.filter((r) => r.state === state);

  if (mine.length === 0) {
    return null;
  }

  return (
    <div>
      <Group gap="xs" mb={4}>
        <Badge color={colour} variant="light">{label}</Badge>
        <Text size="sm" c="dimmed">{mine.length}</Text>
      </Group>

      {help && <Text size="xs" c="dimmed" mb="xs">{help}</Text>}

      <Table.ScrollContainer minWidth={640}>
        <Table striped withTableBorder>
          <Table.Thead>
            <Table.Tr>
              <Table.Th style={{ whiteSpace: 'nowrap', width: '1%' }}>{t('import.colWhere')}</Table.Th>
              <Table.Th style={{ whiteSpace: 'nowrap', width: '1%' }}>{t('import.colCategory')}</Table.Th>
              <Table.Th>{t('import.colWhat')}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {mine.map((row) => (
              <Table.Tr key={`${row.sheet}:${row.row}`}>
                <Table.Td style={{ whiteSpace: 'nowrap' }}>
                  <Text size="xs" c="dimmed">{row.sheet}</Text>
                  <Text size="xs" c="dimmed">row {row.row}</Text>
                </Table.Td>
                <Table.Td style={{ whiteSpace: 'nowrap' }}>{row.name}</Table.Td>
                <Table.Td>
                  {row.reason && <Text size="sm">{row.reason}</Text>}
                  {row.changes.length > 0 && (
                    <List size="xs" spacing={2} listStyleType="none" c={row.reason ? 'dimmed' : undefined}>
                      {row.changes.map((change) => (
                        <List.Item key={change}><Code>{change}</Code></List.Item>
                      ))}
                    </List>
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </div>
  );
}
