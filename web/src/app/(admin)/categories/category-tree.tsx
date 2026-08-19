'use client';

import { useEffect, useState, useTransition } from 'react';
import Link from 'next/link';
import {
  ActionIcon, Badge, Box, Button, Card, Code, Group, Modal, Paper, Select, Stack, Text, Tooltip,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import {
  IconArrowsMove, IconChevronDown, IconChevronRight, IconGripVertical, IconHistory,
  IconPencil, IconPlus, IconTrash,
} from '@tabler/icons-react';
import type { CategoryNode } from '@/lib/categories';
import { canDrop, countDescendants } from '@/lib/tree-utils';
import { deleteCategory, moveCategory } from './actions';

const COLLAPSED_KEY = 'deboet.collapsedCategories';

interface Props {
  nodes: CategoryNode[];
  options: { value: string; label: string }[];
}

export function CategoryTree({ nodes, options }: Props) {
  const [moving, setMoving] = useState<CategoryNode | null>(null);
  const [deleting, setDeleting] = useState<CategoryNode | null>(null);
  const [newParent, setNewParent] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();

  // Remembered because the whole tree re-renders after every mutation; without this,
  // saving an edit would silently expand everything again.
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const [dragged, setDragged] = useState<string | null>(null);
  const [dropTarget, setDropTarget] = useState<string | null | undefined>(undefined);

  // Read on mount rather than during render, so server and client markup match.
  useEffect(() => {
    try {
      const stored = localStorage.getItem(COLLAPSED_KEY);
      if (stored) setCollapsed(new Set(JSON.parse(stored) as string[]));
    } catch {
      // A corrupt entry should not stop the page rendering.
    }
  }, []);

  function persist(next: Set<string>) {
    try {
      localStorage.setItem(COLLAPSED_KEY, JSON.stringify([...next]));
    } catch {
      // Private browsing can refuse writes; collapsing still works for this session.
    }
    return next;
  }

  function toggle(id: string) {
    setCollapsed((current) => {
      const next = new Set(current);
      if (!next.delete(id)) next.add(id);
      return persist(next);
    });
  }

  function collapseAll() {
    const withChildren = new Set<string>();
    const walk = (list: CategoryNode[]) => {
      for (const node of list) {
        if ((node.children ?? []).length > 0) withChildren.add(node.id);
        walk(node.children ?? []);
      }
    };
    walk(nodes);
    setCollapsed(persist(withChildren));
  }

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

  function drop(targetId: string | null) {
    const sourceId = dragged;
    setDragged(null);
    setDropTarget(undefined);

    if (!sourceId || !canDrop(nodes, sourceId, targetId)) return;

    startTransition(async () => {
      report(await moveCategory(sourceId, targetId), 'Category moved.');
    });
  }

  return (
    <>
      <Group>
        <Button component={Link} href="/categories/new" leftSection={<IconPlus size={16} />}>
          New category
        </Button>
        <Button variant="default" onClick={() => setCollapsed(persist(new Set()))}>
          Expand all
        </Button>
        <Button variant="default" onClick={collapseAll}>
          Collapse all
        </Button>
      </Group>

      {nodes.length === 0 && <Text c="dimmed">No categories yet.</Text>}

      <Stack gap="xs">
        {nodes.map((node) => (
          <Branch
            key={node.id}
            node={node}
            depth={0}
            nodes={nodes}
            collapsed={collapsed}
            onToggle={toggle}
            dragged={dragged}
            dropTarget={dropTarget}
            onDragStart={setDragged}
            onDragEnd={() => { setDragged(null); setDropTarget(undefined); }}
            onDragOverNode={setDropTarget}
            onDrop={drop}
            onMove={(n) => { setMoving(n); setNewParent(null); }}
            onDelete={setDeleting}
          />
        ))}
      </Stack>

      {/* Only shown mid-drag: dropping here detaches the branch to the top level. */}
      {dragged !== null && (
        <Paper
          withBorder
          p="md"
          mt="sm"
          style={{
            borderStyle: 'dashed',
            borderColor: dropTarget === null ? 'var(--mantine-color-blue-5)' : undefined,
            background: dropTarget === null ? 'var(--mantine-color-blue-light)' : undefined,
          }}
          onDragOver={(e) => { e.preventDefault(); setDropTarget(null); }}
          onDrop={(e) => { e.preventDefault(); drop(null); }}
        >
          <Text size="sm" c="dimmed" ta="center">Drop here to make it a root category</Text>
        </Paper>
      )}

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

interface BranchProps {
  node: CategoryNode;
  depth: number;
  nodes: CategoryNode[];
  collapsed: Set<string>;
  onToggle: (id: string) => void;
  dragged: string | null;
  dropTarget: string | null | undefined;
  onDragStart: (id: string) => void;
  onDragEnd: () => void;
  onDragOverNode: (id: string) => void;
  onDrop: (id: string) => void;
  onMove: (n: CategoryNode) => void;
  onDelete: (n: CategoryNode) => void;
}

function Branch(props: BranchProps) {
  const { node, depth, nodes, collapsed, onToggle, dragged, dropTarget } = props;

  const children = node.children ?? [];
  const hasChildren = children.length > 0;
  const isCollapsed = collapsed.has(node.id);
  // The whole subtree vanishes, not just the direct children.
  const hiddenCount = countDescendants(node);
  const isDragging = dragged === node.id;
  const isValidTarget = dragged !== null && canDrop(nodes, dragged, node.id);
  const isHovered = dropTarget === node.id && isValidTarget;

  return (
    <>
      <Card
        withBorder
        padding="xs"
        ml={depth * 24}
        draggable
        onDragStart={(e) => {
          e.dataTransfer.effectAllowed = 'move';
          props.onDragStart(node.id);
        }}
        onDragEnd={props.onDragEnd}
        onDragOver={(e) => {
          // preventDefault is what marks this element a valid drop target.
          if (!isValidTarget) return;
          e.preventDefault();
          e.stopPropagation();
          props.onDragOverNode(node.id);
        }}
        onDrop={(e) => {
          if (!isValidTarget) return;
          e.preventDefault();
          e.stopPropagation();
          props.onDrop(node.id);
        }}
        style={{
          opacity: isDragging ? 0.4 : 1,
          cursor: 'grab',
          // Inactive categories are called out in red. The badge stays, so the state is never
          // conveyed by colour alone.
          borderColor: isHovered
            ? 'var(--mantine-color-blue-5)'
            : node.isActive
              ? undefined
              : 'var(--mantine-color-red-4)',
          background: isHovered ? 'var(--mantine-color-blue-light)' : undefined,
        }}
      >
        <Group justify="space-between" wrap="nowrap">
          <Group gap="xs" wrap="nowrap">
            <IconGripVertical size={16} style={{ opacity: 0.4, flexShrink: 0 }} />

            {hasChildren ? (
              <ActionIcon
                variant="subtle"
                size="sm"
                aria-label={isCollapsed ? 'Expand' : 'Collapse'}
                onClick={() => onToggle(node.id)}
              >
                {isCollapsed ? <IconChevronRight size={16} /> : <IconChevronDown size={16} />}
              </ActionIcon>
            ) : (
              <Box w={26} />
            )}

            <Text fw={500} c={node.isActive ? undefined : 'red'}>{node.name}</Text>
            <Code>{node.pathSlug}</Code>

            {!node.isActive && <Badge color="red" size="sm">inactive</Badge>}
            {!node.isMenu && <Badge color="yellow" size="sm">off menu</Badge>}
            {hasChildren && isCollapsed && (
              <Badge color="gray" variant="light" size="sm">
                {hiddenCount} hidden
              </Badge>
            )}
          </Group>

          {/* draggable={false} so pressing a button never starts a drag. */}
          <Group gap={4} wrap="nowrap" draggable={false} onDragStart={(e) => e.preventDefault()}>
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
              <ActionIcon variant="subtle" onClick={() => props.onMove(node)}>
                <IconArrowsMove size={16} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label="Delete">
              <ActionIcon variant="subtle" color="red" onClick={() => props.onDelete(node)}>
                <IconTrash size={16} />
              </ActionIcon>
            </Tooltip>
          </Group>
        </Group>
      </Card>

      {!isCollapsed &&
        children.map((child) => (
          <Branch key={child.id} {...props} node={child} depth={depth + 1} />
        ))}
    </>
  );
}
