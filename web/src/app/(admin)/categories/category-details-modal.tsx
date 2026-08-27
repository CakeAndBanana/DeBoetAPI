'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import {
  Alert, Anchor, Badge, Breadcrumbs, Button, Code, Divider, Group, Loader, Modal, Stack, Text,
} from '@mantine/core';
import { IconExternalLink, IconHistory, IconPencil } from '@tabler/icons-react';
import { useT } from '@/i18n/client';
import { categoryDetails, type CategoryDetails } from './actions';

interface Props {
  /** The category to describe, or null when the modal is closed. */
  categoryId: string | null;
  /** Shown while the details load, so the modal has a title immediately. */
  categoryName: string;
  siteUrl: string;
  onClose: () => void;
}

export function CategoryDetailsModal({ categoryId, categoryName, siteUrl, onClose }: Props) {
  const t = useT();
  type Loaded = { status: 'error'; message: string } | { status: 'ready'; details: CategoryDetails };
  const [loaded, setLoaded] = useState<{ id: string; result: Loaded } | null>(null);

  // Fetched on open rather than with the tree: the tree can be hundreds of categories, and
  // pre-loading a detail and an audit page for each would cost far more than it saves.
  useEffect(() => {
    if (!categoryId) return;

    let current = true;

    categoryDetails(categoryId).then(
      (result) => current && setLoaded({
        id: categoryId,
        result: result.ok
          ? { status: 'ready', details: result.details }
          : { status: 'error', message: result.message },
      }),
      () => current && setLoaded({
        id: categoryId,
        result: { status: 'error', message: t('details.loadFailedBody') },
      }),
    );

    // Guards against a stale response landing after the user has opened a different category.
    return () => { current = false; };
  }, [categoryId, t]);

  // Derived rather than stored, so opening a second category cannot show the first one's
  // record for a frame while the new request is in flight.
  const state: Loaded | { status: 'loading' } =
    loaded && loaded.id === categoryId ? loaded.result : { status: 'loading' };

  return (
    <Modal opened={!!categoryId} onClose={onClose} title={categoryName} size="lg">
      {state.status === 'loading' && (
        <Group justify="center" py="xl"><Loader size="sm" /></Group>
      )}

      {state.status === 'error' && <Alert color="red" title={t('details.loadFailed')}>{state.message}</Alert>}

      {state.status === 'ready' && <Details details={state.details} siteUrl={siteUrl} onClose={onClose} />}
    </Modal>
  );
}

function Details({
  details, siteUrl, onClose,
}: {
  details: CategoryDetails;
  siteUrl: string;
  onClose: () => void;
}) {
  const t = useT();
  const url = `${siteUrl}/${details.pathSlug}`;

  return (
    <Stack gap="md">
      <Field label={t('details.where')}>
        <Breadcrumbs separator="›" styles={{ separator: { marginInline: 6 } }}>
          {details.trail.map((c, i) => (
            <Text key={c.id} size="sm" fw={i === details.trail.length - 1 ? 600 : 400}>
              {c.name}
            </Text>
          ))}
        </Breadcrumbs>
        {details.trail.length === 1 && (
          <Text size="xs" c="dimmed">{t('details.root')}</Text>
        )}
      </Field>

      <Field label={t('details.address')}>
        {/*
          Its own address, stated whether or not it forwards — it is still what you would put
          in a link. Only the row below opens a page, and that one goes where a visitor
          actually ends up.
        */}
        {details.redirectTo ? (
          <Code>{details.pathSlug}</Code>
        ) : (
          <Anchor href={url} target="_blank" rel="noreferrer" size="sm">
            <Group gap={4} wrap="nowrap">
              <Code>{details.pathSlug}</Code>
              <IconExternalLink size={14} />
            </Group>
          </Anchor>
        )}
        <Text size="xs" c="dimmed">{t('details.addressHelp', { slug: details.slug })}</Text>
      </Field>

      <Field label={t('details.state')}>
        <Group gap="xs">
          <Badge color={details.isActive ? 'green' : 'red'} size="sm">
            {details.isActive ? t('details.active') : t('details.inactive')}
          </Badge>
          <Badge color={details.isMenu ? 'green' : 'blue'} size="sm">
            {details.isMenu ? t('details.inMenu') : t('details.offMenu')}
          </Badge>
          <Text size="sm" c="dimmed">
            {details.childCount === 0
              ? t('details.noChildren')
              : details.childCount === 1
                ? t('details.childCountOne')
                : t('details.childCount', { count: details.childCount })}
          </Text>
        </Group>
      </Field>

      {details.secondaryParents.length > 0 && (
        <Field label={t('details.alsoUnder')}>
          <Stack gap={2}>
            {details.secondaryParents.map((p) => (
              <Text key={p.id} size="sm">
                {p.name} <Code>{p.pathSlug}</Code>
              </Text>
            ))}
          </Stack>
        </Field>
      )}

      <Field label={t('details.redirectsTo')}>
        {details.redirectTo ? (
          <Text size="sm">
            {details.redirectTo.name}{' '}
            <Anchor
              href={`${siteUrl}/${details.redirectTo.pathSlug}`}
              target="_blank"
              rel="noreferrer"
            >
              <Code>{details.redirectTo.pathSlug}</Code>
              <IconExternalLink size={14} style={{ marginInlineStart: 4, verticalAlign: -2 }} />
            </Anchor>
          </Text>
        ) : (
          <Text size="sm" c="dimmed">{t('details.noRedirect')}</Text>
        )}
      </Field>

      <Divider />

      <Field label={t('categories.fieldMetaTitle')}>
        {details.metaTitle
          ? <Text size="sm">{details.metaTitle}</Text>
          : <Text size="sm" c="dimmed" fs="italic">Not set</Text>}
      </Field>

      <Field label={t('categories.fieldMetaDesc')}>
        {details.metaDescription
          ? <Text size="sm">{details.metaDescription}</Text>
          : <Text size="sm" c="dimmed" fs="italic">Not set</Text>}
      </Field>

      <Divider />

      <Field label={t('details.created')}>
        <Text size="sm">
          {formatWhen(details.createdAt)}
          {details.createdBy && <> {t('details.by')} <strong>{details.createdBy}</strong></>}
        </Text>
      </Field>

      <Field label={t('details.lastEdited')}>
        <Text size="sm">
          {formatWhen(details.updatedAt)}
          {details.updatedBy && <> {t('details.by')} <strong>{details.updatedBy}</strong></>}
        </Text>
        {details.lastAction && (
          <Text size="xs" c="dimmed">
            {details.lastAction}
            {details.lastSummary ? ` — ${details.lastSummary}` : ''}
          </Text>
        )}
        {details.changeCount === null && (
          <Text size="xs" c="dimmed" fs="italic">{t('details.noHistoryAccess')}</Text>
        )}
      </Field>

      <Group justify="flex-end" mt="xs">
        <Button
          variant="default"
          component={Link}
          href={`/categories/${details.id}/history`}
          leftSection={<IconHistory size={16} />}
          onClick={onClose}
        >
          {t('details.fullHistory')}
        </Button>
        <Button
          component={Link}
          href={`/categories/${details.id}/edit`}
          leftSection={<IconPencil size={16} />}
          onClick={onClose}
        >
          {t('common.edit')}
        </Button>
      </Group>
    </Stack>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <Text size="xs" c="dimmed" tt="uppercase" fw={600} lh={1.6}>{label}</Text>
      {children}
    </div>
  );
}

/**
 * Rendered on the client so it lands in the reader's own timezone. The API sends UTC offsets,
 * which a server render would resolve against the container's clock instead.
 */
function formatWhen(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.valueOf()) ? iso : date.toLocaleString();
}
