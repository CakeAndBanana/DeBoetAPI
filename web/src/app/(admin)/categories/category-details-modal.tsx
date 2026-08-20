'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import {
  Alert, Anchor, Badge, Breadcrumbs, Button, Code, Divider, Group, Loader, Modal, Stack, Text,
} from '@mantine/core';
import { IconExternalLink, IconHistory, IconPencil } from '@tabler/icons-react';
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
        result: { status: 'error', message: 'Could not load this category.' },
      }),
    );

    // Guards against a stale response landing after the user has opened a different category.
    return () => { current = false; };
  }, [categoryId]);

  // Derived rather than stored, so opening a second category cannot show the first one's
  // record for a frame while the new request is in flight.
  const state: Loaded | { status: 'loading' } =
    loaded && loaded.id === categoryId ? loaded.result : { status: 'loading' };

  return (
    <Modal opened={!!categoryId} onClose={onClose} title={categoryName} size="lg">
      {state.status === 'loading' && (
        <Group justify="center" py="xl"><Loader size="sm" /></Group>
      )}

      {state.status === 'error' && <Alert color="red" title="Could not load">{state.message}</Alert>}

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
  const url = `${siteUrl}/${details.pathSlug}`;

  return (
    <Stack gap="md">
      <Field label="Where it sits">
        <Breadcrumbs separator="›" styles={{ separator: { marginInline: 6 } }}>
          {details.trail.map((c, i) => (
            <Text key={c.id} size="sm" fw={i === details.trail.length - 1 ? 600 : 400}>
              {c.name}
            </Text>
          ))}
        </Breadcrumbs>
        {details.trail.length === 1 && (
          <Text size="xs" c="dimmed">A root category.</Text>
        )}
      </Field>

      <Field label="Address">
        <Anchor href={url} target="_blank" rel="noreferrer" size="sm">
          <Group gap={4} wrap="nowrap">
            <Code>{details.pathSlug}</Code>
            <IconExternalLink size={14} />
          </Group>
        </Anchor>
        <Text size="xs" c="dimmed">
          Own segment <Code>{details.slug}</Code>, prefixed by every ancestor.
        </Text>
      </Field>

      <Field label="State">
        <Group gap="xs">
          <Badge color={details.isActive ? 'green' : 'red'} size="sm">
            {details.isActive ? 'active' : 'inactive'}
          </Badge>
          <Badge color={details.isMenu ? 'green' : 'blue'} size="sm">
            {details.isMenu ? 'in menu' : 'off menu'}
          </Badge>
          <Text size="sm" c="dimmed">
            {details.childCount === 0
              ? 'no child categories'
              : `${details.childCount} child ${details.childCount === 1 ? 'category' : 'categories'}`}
          </Text>
        </Group>
      </Field>

      {details.secondaryParents.length > 0 && (
        <Field label="Also appears under">
          <Stack gap={2}>
            {details.secondaryParents.map((p) => (
              <Text key={p.id} size="sm">
                {p.name} <Code>{p.pathSlug}</Code>
              </Text>
            ))}
          </Stack>
        </Field>
      )}

      <Divider />

      <Field label="Meta title">
        {details.metaTitle
          ? <Text size="sm">{details.metaTitle}</Text>
          : <Text size="sm" c="dimmed" fs="italic">Not set</Text>}
      </Field>

      <Field label="Meta description">
        {details.metaDescription
          ? <Text size="sm">{details.metaDescription}</Text>
          : <Text size="sm" c="dimmed" fs="italic">Not set</Text>}
      </Field>

      <Divider />

      <Field label="Created">
        <Text size="sm">
          {formatWhen(details.createdAt)}
          {details.createdBy && <> by <strong>{details.createdBy}</strong></>}
        </Text>
      </Field>

      <Field label="Last edited">
        <Text size="sm">
          {formatWhen(details.updatedAt)}
          {details.updatedBy && <> by <strong>{details.updatedBy}</strong></>}
        </Text>
        {details.lastAction && (
          <Text size="xs" c="dimmed">
            {details.lastAction}
            {details.lastSummary ? ` — ${details.lastSummary}` : ''}
          </Text>
        )}
        {details.changeCount === null && (
          <Text size="xs" c="dimmed" fs="italic">
            Your account cannot read the change history, so no names are shown.
          </Text>
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
          Full history
        </Button>
        <Button
          component={Link}
          href={`/categories/${details.id}/edit`}
          leftSection={<IconPencil size={16} />}
          onClick={onClose}
        >
          Edit
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
