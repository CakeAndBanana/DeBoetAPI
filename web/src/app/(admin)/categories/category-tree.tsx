'use client';

import { useState, useTransition } from 'react';
import Link from 'next/link';
import {
  ActionIcon, Badge, Button, Card, Code, Group, Modal, Select, Stack, Text, Tooltip,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import {
  IconArrowsMove, IconHistory, IconPencil, IconPlus, IconTrash,
} from '@tabler/icons-react';
import type { CategoryNode } from '@/lib/categories';
import { deleteCategory, moveCategory } from './actions';

interface Props {
  nodes: CategoryNode[];
  options: { value: string; label: string }[];
}

export function CategoryTree({ nodes, options }: Props) {
  const [moving, setMoving] = useState<CategoryNode | null>(null);
  const [deleting, setDeleting] = useState<CategoryNode | null>(null);
  const [newParent, setNewParent] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  function report(result: { ok: boolean; message?: string; promoted?: string[] }, success: string) {
    if (!result.ok) {
      notifications.show({ color: 'red', title: 'Refused', message: result.message ?? 'Failed' });
      return;
    }

    const promoted = result.promoted?.length
      ? ` ${result.promoted.length} child category was promoted to the grandparent.`
      : '';
    notifications.show({ color: 'green', title: 'Done', message: success + promoted });
  }

  return (
    <>
      <Group>
        <Button component={Link} href="/categories/new" leftSection={<IconPlus size={16} />}>
          New category
        </Button>
      </Group>

      {nodes.length === 0 && <Text c="dimmed">No categories yet.</Text>}

      <Stack gap="xs">
        {nodes.map((node) => (
          <Branch
            key={node.id}
            node={node}
            depth={0}
            onMove={(n) => { setMoving(n); setNewParent(null); }}
            onDelete={setDeleting}
          />
        ))}
      </Stack>

      <Modal opened={!!moving} onClose={() => setMoving(null)} title={`Move "${moving?.name}"`}>
        <Stack>
          <Text size="sm" c="dimmed">
            Everything beneath it moves too, and every descendant route is rewritten.
          </Text>
          <Select
            label="New parent"
            placeholder="(make it a root category)"
            data={options.filter((o) => o.value !== moving?.id)}
            value={newParent}
            onChange={setNewParent}
            clearable
            searchable
          />
          <Button
            loading={pending}
            onClick={() =>
              startTransition(async () => {
                const result = await moveCategory(moving!.id, newParent);
                report(result, 'Category moved.');
                if (result.ok) setMoving(null);
              })
            }
          >
            Move
          </Button>
        </Stack>
      </Modal>

      <Modal opened={!!deleting} onClose={() => setDeleting(null)} title={`Delete "${deleting?.name}"`}>
        <Stack>
          <Text size="sm">
            Its children are <strong>not</strong> deleted — they are promoted to this
            category&apos;s parent, and their routes are rewritten to match.
          </Text>
          <Button
            color="red"
            loading={pending}
            onClick={() =>
              startTransition(async () => {
                const result = await deleteCategory(deleting!.id);
                report(result, 'Category deleted.');
                if (result.ok) setDeleting(null);
              })
            }
          >
            Delete
          </Button>
        </Stack>
      </Modal>
    </>
  );
}

function Branch({
  node, depth, onMove, onDelete,
}: {
  node: CategoryNode;
  depth: number;
  onMove: (n: CategoryNode) => void;
  onDelete: (n: CategoryNode) => void;
}) {
  return (
    <>
      <Card withBorder padding="xs" ml={depth * 24}>
        <Group justify="space-between" wrap="nowrap">
          <Group gap="xs" wrap="nowrap">
            <Text fw={500}>{node.name}</Text>
            <Code>{node.pathSlug}</Code>
            {!node.isActive && <Badge color="gray" size="sm">inactive</Badge>}
            {!node.isMenu && <Badge color="yellow" size="sm">off menu</Badge>}
          </Group>

          <Group gap={4} wrap="nowrap">
            <Tooltip label="Add child">
              <ActionIcon component={Link} href={`/categories/new?parent=${node.id}`} variant="subtle">
                <IconPlus size={16} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Edit">
              <ActionIcon component={Link} href={`/categories/${node.id}/edit`} variant="subtle">
                <IconPencil size={16} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="History">
              <ActionIcon component={Link} href={`/categories/${node.id}/history`} variant="subtle">
                <IconHistory size={16} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Move">
              <ActionIcon variant="subtle" onClick={() => onMove(node)}>
                <IconArrowsMove size={16} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete">
              <ActionIcon variant="subtle" color="red" onClick={() => onDelete(node)}>
                <IconTrash size={16} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </Group>
      </Card>

      {(node.children ?? []).map((child) => (
        <Branch key={child.id} node={child} depth={depth + 1} onMove={onMove} onDelete={onDelete} />
      ))}
    </>
  );
}
