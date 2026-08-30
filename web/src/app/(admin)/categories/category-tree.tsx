'use client';

import { useEffect, useMemo, useRef, useState, useTransition } from 'react';
import Link from 'next/link';
import {
  ActionIcon, Anchor, Badge, Box, Button, Card, Code, Divider, Group, Highlight, Modal, Paper,
  Stack, Text, TextInput, Tooltip, UnstyledButton,
} from '@mantine/core';
import { notifications } from '@mantine/notifications';
import {
  IconArrowDown, IconArrowsMove, IconArrowUp, IconChevronDown, IconChevronRight,
  IconFileSpreadsheet, IconGripVertical, IconHistory, IconPencil, IconPlus, IconSearch,
  IconTrash, IconUpload, IconX,
} from '@tabler/icons-react';
import type { CategoryNode, CategoryOption } from '@/lib/categories';
import {
  branchColours, canDrop, collectSubtreeIds, countDescendants, countNodes, dropTargetFrom,
  filterTree, findNode, indexOf, placementFor, searchTerms, stepFor, type DropMode,
} from '@/lib/tree-utils';
import { CategoryPicker } from '@/components/category-picker';
import { fileNameFrom } from '@/lib/download';
import { deleteCategory, moveCategory } from './actions';
import { CategoryDetailsModal } from './category-details-modal';
import { CategoryForm } from './category-form';
import { useT } from '@/i18n/client';

const COLLAPSED_KEY = 'deboet.collapsedCategories';

/**
 * A wash of the brand green at the trailing end of a row.
 *
 * Held flat for most of the width so the name, the route and the badges all sit on plain white,
 * then eased in over the last third. Faint on purpose: the badges are the only colour on a row
 * that carries meaning, and a background strong enough to notice is strong enough to argue with
 * them.
 */
const ROW_TINT = 'linear-gradient(90deg,'
  + ' var(--mantine-color-body) 0%,'
  + ' var(--mantine-color-body) 62%,'
  + ' var(--mantine-color-myColor-0) 100%)';

/** Stands in for the collapsed set while searching: nothing is collapsed. */
const EXPANDED: Set<string> = new Set();

interface Props {
  nodes: CategoryNode[];
  options: CategoryOption[];
  siteUrl: string;
}

export function CategoryTree({ nodes, options, siteUrl }: Props) {
  const t = useT();
  const [viewing, setViewing] = useState<CategoryNode | null>(null);
  const [moving, setMoving] = useState<CategoryNode | null>(null);
  const [deleting, setDeleting] = useState<CategoryNode | null>(null);
  /**
   * The category being created, or null when the dialog is shut. `parent` is the node it will
   * hang under, so a null *inside* this object means a new root — which is why the whole thing
   * is nullable rather than just tracking the parent id.
   */
  const [creating, setCreating] = useState<{ parent: CategoryNode | null } | null>(null);
  const [exporting, setExporting] = useState(false);
  const [newParent, setNewParent] = useState<string | null>(null);
  const movingSubtree = moving
    ? collectSubtreeIds(findNode(nodes, moving.id) ?? moving)
    : undefined;
  const [pending, startTransition] = useTransition();

  // Assigned from the unfiltered tree, so searching narrows what is shown without repainting
  // the branches that survive.
  const tints = useMemo(() => branchColours(nodes.map((n) => n.id)), [nodes]);

  const [search, setSearch] = useState('');
  const terms = useMemo(() => searchTerms(search), [search]);
  const visible = useMemo(() => filterTree(nodes, terms), [nodes, terms]);
  const searching = terms.length > 0;

  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const [dragged, setDragged] = useState<string | null>(null);
  const [dropTarget, setDropTarget] = useState<string | null | undefined>(undefined);
  const [dropMode, setDropMode] = useState<DropMode>('inside');

  useEffect(() => {
    try {
      const stored = localStorage.getItem(COLLAPSED_KEY);
      // eslint-disable-next-line react-hooks/set-state-in-effect
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

  /**
   * Opens the creation dialog. A parent is expanded on the way in, so the child that is about
   * to be added lands somewhere visible rather than inside a branch that is still folded shut.
   */
  function beginCreate(parent: CategoryNode | null) {
    if (parent) {
      setCollapsed((current) => {
        const next = new Set(current);
        return next.delete(parent.id) ? persist(next) : current;
      });
    }
    setCreating({ parent });
  }

  /**
   * Downloads the whole tree as a spreadsheet.
   *
   * Fetched into a blob rather than done with a plain link, because the API has to walk every
   * category and render a workbook before a single byte comes back. A link would leave the page
   * sitting there with no sign anything was happening, and would replace the tree with a bare
   * browser error page if it failed.
   */
  async function exportTree() {
    setExporting(true);

    try {
      const response = await fetch('/api/categories/export');

      if (!response.ok) {
        notifications.show({
          color: 'red',
          title: t('common.refused'),
          message: (await response.text()) || t('categories.exportFailed'),
        });
        return;
      }

      const url = URL.createObjectURL(await response.blob());
      const link = document.createElement('a');
      link.href = url;
      link.download = fileNameFrom(response.headers.get('content-disposition'), 'categories.xlsx');
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      notifications.show({
        color: 'red',
        title: t('common.refused'),
        message: t('categories.exportFailed'),
      });
    } finally {
      setExporting(false);
    }
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
      notifications.show({ color: 'red', title: t('common.refused'), message: result.message ?? t('categories.failed') });
      return;
    }

    const promoted = result.promoted?.length
      ? t('categories.promoted', { count: result.promoted.length })
      : '';
    notifications.show({ color: 'green', title: t('common.done'), message: success + promoted });
  }

  function drop(targetId: string | null, source: string | null = dragged, mode: DropMode = 'inside') {
    const sourceId = source;
    setDragged(null);
    setDropTarget(undefined);
    if (!sourceId) return;

    // Beside a category is a reorder among its siblings; inside it is a reparent. The two are
    // one call to the API, which is what keeps a drag between two branches from being two
    // half-finished operations.
    if (targetId !== null && mode !== 'inside') {
      const placement = placementFor(nodes, sourceId, targetId, mode);
      if (!placement) return;

      startTransition(async () => {
        report(
          await moveCategory(sourceId, placement.parentId, placement.position),
          t('categories.reordered'),
        );
      });
      return;
    }

    if (!canDrop(nodes, sourceId, targetId)) return;

    startTransition(async () => {
      report(await moveCategory(sourceId, targetId), t('categories.moved'));
    });
  }

  /** One step up or down among a category's own siblings, for the arrow controls. */
  function step(node: CategoryNode, delta: -1 | 1) {
    const placement = stepFor(nodes, node.id, delta);
    if (!placement) return;

    startTransition(async () => {
      report(
        await moveCategory(node.id, placement.parentId, placement.position),
        t('categories.reordered'),
      );
    });
  }

  /**
   * Dragging, for every input device.
   *
   * Pointer events rather than HTML5 drag-and-drop. The native API does not fire on touch at
   * all, and worse, leaving `draggable` on the card actively broke the gesture: a long press
   * on a draggable element makes Android Chrome start its *own* drag, which fires
   * pointercancel and tears down ours mid-move. One path, no `draggable` attribute, nothing to
   * fight over — and a mouse behaves identically.
   *
   * The target is resolved with elementFromPoint rather than by listening on each card:
   * pointer capture sends every move to the handle that started the drag, so the cards below
   * never hear about it.
   */
  const dragging = useRef<string | null>(null);
  const autoScroll = useRef(0);

  const targetUnder = (x: number, y: number, source: string | null) =>
    dropTargetFrom(document.elementFromPoint(x, y), nodes, source, y);

  /**
   * Scrolls the page when the pointer nears an edge.
   *
   * Without this a phone drag can only reach what is already on screen: the gesture is
   * captured, so the usual swipe-to-scroll is unavailable for as long as it lasts.
   */
  function edgeScroll(y: number) {
    cancelAnimationFrame(autoScroll.current);
    if (dragging.current === null) return;

    const margin = 72;
    const speed = y < margin ? -12 : y > window.innerHeight - margin ? 12 : 0;
    if (speed === 0) return;

    const step = () => {
      if (dragging.current === null) return;
      window.scrollBy(0, speed);
      autoScroll.current = requestAnimationFrame(step);
    };
    autoScroll.current = requestAnimationFrame(step);
  }

  function beginDrag(event: React.PointerEvent, id: string) {
    if (!draggingAllowed) return;

    // Claims the gesture from the page's own scrolling, which otherwise wins on touch.
    event.preventDefault();
    event.currentTarget.setPointerCapture(event.pointerId);
    dragging.current = id;
    setDragged(id);
  }

  function moveDrag(event: React.PointerEvent) {
    if (dragging.current === null) return;
    event.preventDefault();

    const { id, valid, mode } = targetUnder(event.clientX, event.clientY, dragging.current);
    setDropTarget(valid ? id : undefined);
    setDropMode(mode);
    edgeScroll(event.clientY);
  }

  function endDrag(event: React.PointerEvent) {
    const source = dragging.current;
    if (source === null) return;

    dragging.current = null;
    cancelAnimationFrame(autoScroll.current);

    const { id, valid, mode } = targetUnder(event.clientX, event.clientY, source);
    if (valid) {
      drop(id, source, mode);
      return;
    }

    setDragged(null);
    setDropTarget(undefined);
  }

  function cancelDrag() {
    dragging.current = null;
    cancelAnimationFrame(autoScroll.current);
    setDragged(null);
    setDropTarget(undefined);
  }

  const draggingAllowed = !searching;

  return (
    <>
      <Group justify="space-between" wrap="nowrap" align="flex-start">
        <Group>
          <TextInput
            w={280}
            placeholder={t('categories.search')}
            leftSection={<IconSearch size={16} />}
            value={search}
            onChange={(e) => setSearch(e.currentTarget.value)}
            rightSection={search ? (
              <ActionIcon variant="subtle" aria-label={t('categories.clearSearch')} onClick={() => setSearch('')}>
                <IconX size={16} />
              </ActionIcon>
            ) : null}
          />

          {/* Collapsing is meaningless while a search is forcing branches open. */}
          <Button
            variant="default"
            disabled={searching}
            onClick={() => setCollapsed(persist(new Set()))}
          >
            {t('categories.expandAll')}
          </Button>
          <Button variant="default" disabled={searching} onClick={collapseAll}>
            {t('categories.collapseAll')}
          </Button>
        </Group>

        <Group gap="xs" wrap="nowrap">
          <Button
            variant="default"
            onClick={exportTree}
            loading={exporting}
            leftSection={<IconFileSpreadsheet size={16} />}
          >
            {exporting ? t('categories.exporting') : t('categories.export')}
          </Button>

          <Button
            component={Link}
            href="/categories/import"
            variant="default"
            leftSection={<IconUpload size={16} />}
          >
            {t('categories.import')}
          </Button>

          <Button onClick={() => beginCreate(null)} leftSection={<IconPlus size={16} />}>
            {t('categories.new')}
          </Button>
        </Group>
      </Group>

      {searching && (
        <Text size="sm" c="dimmed">
          {countNodes(visible) === 0
            ? t('categories.noMatch')
            : t('categories.matchCount', { shown: countNodes(visible), total: countNodes(nodes) })}
          {' '}{t('categories.dragOffWhileSearching')}
        </Text>
      )}

      <Divider />

      {nodes.length === 0 && <Text c="dimmed">{t('categories.empty')}</Text>}

      {/* gap 0: each row carries its own bottom padding so the guide lines stay unbroken. */}
      <Stack gap={0}>
        {visible.map((node) => (
          <Branch
            key={node.id}
            node={node}
            depth={0}
            nodes={nodes}
            collapsed={searching ? EXPANDED : collapsed}
            onToggle={toggle}
            terms={terms}
            dragged={dragged}
            dropTarget={dropTarget}
            onMove={(n) => { setMoving(n); setNewParent(null); }}
            onDelete={setDeleting}
            onCreate={beginCreate}
            onView={setViewing}
            siteUrl={siteUrl}
            draggingAllowed={draggingAllowed}
            onDragBegin={beginDrag}
            onDragMove={moveDrag}
            onDragEnd={endDrag}
            onDragCancel={cancelDrag}
            dropMode={dropMode}
            onStep={step}
            continues={[]}
            tint={tints.get(node.id) ?? 'var(--mantine-color-default-border)'}
          />
        ))}
      </Stack>

      {/* Only shown mid-drag: dropping here detaches the branch to the top level. */}
      {dragged !== null && !searching && (
        <Paper
          withBorder
          data-root-drop
          p="md"
          mt="sm"
          style={{
            borderStyle: 'dashed',
            borderColor: dropTarget === null ? 'var(--mantine-color-teal-5)' : undefined,
            background: dropTarget === null ? 'var(--mantine-color-teal-light)' : undefined,
          }}
        >
          <Text size="sm" c="dimmed" ta="center">{t('categories.dropAsRoot')}</Text>
        </Paper>
      )}

      {/*
        Creation happens over the tree rather than on a page of its own, so the branch you were
        looking at is still there behind the dialog — and still there when it closes, with the
        collapsed state and the scroll position untouched.

        Mounted only while open: a fresh CategoryForm each time is what clears the fields, and
        it is also how the preselected parent gets in, since the form seeds itself once.
      */}
      <Modal
        opened={!!creating}
        onClose={() => setCreating(null)}
        size="lg"
        title={creating?.parent
          ? t('categories.newUnder', { name: creating.parent.name })
          : t('categories.newTitle')}
      >
        {creating && (
          <CategoryForm
            options={options}
            initial={{ parentId: creating.parent?.id ?? null }}
            onClose={() => setCreating(null)}
          />
        )}
      </Modal>

      <CategoryDetailsModal
        categoryId={viewing?.id ?? null}
        categoryName={viewing?.name ?? ''}
        siteUrl={siteUrl}
        onClose={() => setViewing(null)}
      />

      <Modal opened={!!moving} onClose={() => setMoving(null)} title={t('categories.moveTitle', { name: moving?.name ?? '' })}>
        <Stack>
          <Text size="sm" c="dimmed">{t('categories.moveBody')}</Text>
          {/*
            Only categories outside the moving branch are offered. The API rejects a move into
            one's own subtree with a 409, so listing those would just be a dead end.
          */}
          <CategoryPicker
            label={t('categories.moveParent')}
            placeholder={t('categories.moveParentPlaceholder')}
            options={options}
            exclude={movingSubtree}
            value={newParent}
            onChange={setNewParent}
          />
          <Button
            loading={pending}
            onClick={() =>
              startTransition(async () => {
                const result = await moveCategory(moving!.id, newParent);
                report(result, t('categories.moved'));
                if (result.ok) setMoving(null);
              })
            }
          >
            {t('common.move')}
          </Button>
        </Stack>
      </Modal>

      <Modal opened={!!deleting} onClose={() => setDeleting(null)} title={t('categories.deleteTitle', { name: deleting?.name ?? '' })}>
        <Stack>
          <Text size="sm">{t('categories.deleteBody')}</Text>
          <Button
            color="red"
            loading={pending}
            onClick={() =>
              startTransition(async () => {
                const result = await deleteCategory(deleting!.id);
                report(result, t('categories.deleted'));
                if (result.ok) setDeleting(null);
              })
            }
          >
            {t('common.delete')}
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
  onMove: (n: CategoryNode) => void;
  onDelete: (n: CategoryNode) => void;
  /** Opens the creation dialog with this node preselected as the parent. */
  onCreate: (n: CategoryNode) => void;
  onView: (n: CategoryNode) => void;
  siteUrl: string;
  /** Search terms, for highlighting. Empty when not searching. */
  terms: string[];
  draggingAllowed: boolean;
  onDragBegin: (event: React.PointerEvent, id: string) => void;
  onDragMove: (event: React.PointerEvent) => void;
  onDragEnd: (event: React.PointerEvent) => void;
  onDragCancel: () => void;
  /** Which edge of the hovered card the pointer is over, so the indicator lands correctly. */
  dropMode: DropMode;
  onStep: (node: CategoryNode, delta: -1 | 1) => void;
  /**
   * The colour of the branch this row belongs to, seeded from the root category's id. Passed
   * down rather than recomputed, so every row under one root matches without each of them
   * having to know which root that is.
   */
  tint: string;
  /**
   * One entry per indent column. Entry j says whether the ancestor sitting at column j has a
   * further sibling below, which is what decides whether its vertical guide keeps running past
   * this row. The last entry describes this node itself, and drives the tail of its own elbow.
   */
  continues: boolean[];
}

function Branch(props: BranchProps) {
  const t = useT();
  const {
    node, depth, nodes, collapsed, onToggle, dragged, dropTarget, siteUrl, terms, draggingAllowed,
    continues, dropMode, tint,
  } = props;

  // Read from the whole tree, not the filtered view, so the ends of a list are the real ends.
  // Where a visitor actually ends up. A forwarding category has no page of its own, so
  // linking to its own address would open the one place it is guaranteed not to stay.
  const destination = node.redirectToId ? findNode(nodes, node.redirectToId) : null;
  const liveUrl = `${siteUrl}/${(destination ?? node).pathSlug}`;

  const place = indexOf(nodes, node.id);
  const isFirst = !place || place.index <= 0;
  const isLast = !place || place.index >= place.count - 1;

  const children = node.children ?? [];
  const hasChildren = children.length > 0;
  const isCollapsed = collapsed.has(node.id);
  // The whole subtree vanishes, not just the direct children.
  const hiddenCount = countDescendants(node);
  const isDragging = dragged === node.id;
  const isValidTarget = draggingAllowed && dragged !== null && canDrop(nodes, dragged, node.id);
  const isHovered = dropTarget === node.id && isValidTarget;
  // Inside means "become a child", so the card fills; beside means "sit here", so a line is
  // drawn on the edge it would land against.
  const insertAt = isHovered && dropMode !== 'inside' ? dropMode : null;

  // The two publishing flags, read off the border. Both off is its own colour rather than
  // one winning, because "not live and not navigable" is a different situation from either
  // alone. The badges below carry the same information in words, so nothing is colour-only.
  const stateColour =
    !node.isActive && !node.isMenu ? 'var(--mantine-color-violet-5)'
      : !node.isActive ? 'var(--mantine-color-red-4)'
        : !node.isMenu ? 'var(--mantine-color-blue-5)'
          : undefined;

  return (
    <>
      <Group gap={0} wrap="nowrap" align="stretch" pb={4}>
        {continues.map((runsOn, column) => (
          <TreeGuide
            key={column}
            runsOn={runsOn}
            isElbow={column === continues.length - 1}
            colour={tint}
          />
        ))}

      <Card
        withBorder
        padding="xs"
        data-category-id={node.id}
        style={{
          // Takes the width the guide columns leave behind.
          flex: 1,
          minWidth: 0,
          opacity: isDragging ? 0.4 : 1,
          cursor: 'default',
          // Teal, not blue: blue now means "off menu", and a drop target has to stay
          // legible against a tree full of them mid-drag.
          borderColor: isHovered && !insertAt ? 'var(--mantine-color-teal-5)' : stateColour,
          // The drop highlight wins outright: mid-drag, knowing where the card will land matters
          // more than the decoration.
          background: isHovered && !insertAt ? 'var(--mantine-color-teal-light)' : ROW_TINT,
          borderTop: insertAt === 'before' ? '3px solid var(--mantine-color-teal-6)' : undefined,
          borderBottom: insertAt === 'after' ? '3px solid var(--mantine-color-teal-6)' : undefined,
        }}
      >
        <Group justify="space-between" wrap="nowrap" gap="md">
          <Group gap="xs" wrap="nowrap">
            {/*
              The drag handle, for mouse and finger alike. Deliberately just the grip and not
              the whole card: a finger anywhere else must still scroll the page, and
              touchAction none is what stops the browser claiming the gesture first.
            */}
            <span
              onPointerDown={(e) => props.onDragBegin(e, node.id)}
              onPointerMove={props.onDragMove}
              onPointerUp={props.onDragEnd}
              onPointerCancel={props.onDragCancel}
              style={{
                display: 'inline-flex',
                flexShrink: 0,
                touchAction: draggingAllowed ? 'none' : 'auto',
                cursor: draggingAllowed ? 'grab' : 'default',
                padding: 4,
                margin: -4,
              }}
            >
              <IconGripVertical size={16} style={{ opacity: 0.4 }} />
            </span>

            {hasChildren ? (
              <ActionIcon
                variant="subtle"
                size="sm"
                aria-label={isCollapsed ? t('categories.expand') : t('categories.collapse')}
                onClick={() => onToggle(node.id)}
              >
                {isCollapsed ? <IconChevronRight size={16} /> : <IconChevronDown size={16} />}
              </ActionIcon>
            ) : (
              <Box w={26} />
            )}

            {/*
              The name opens the full record. draggable={false} and the click guard keep the
              card's drag-to-move from swallowing it, exactly as with the route link beside it.
            */}
            <UnstyledButton
              draggable={false}
              onDragStart={(e) => e.preventDefault()}
              onClick={(e) => { e.stopPropagation(); props.onView(node); }}
              style={{ cursor: 'pointer' }}
            >
              <Highlight
                fw={500}
                c={node.isActive ? undefined : 'red'}
                style={{ textDecorationStyle: 'dotted' }}
                td="underline"
                highlight={terms}
              >
                {node.name}
              </Highlight>
            </UnstyledButton>
            {/*
              Hidden on a phone: a full route is long enough to push the name, the badges and
              the controls off the screen, and it is the least useful of them there. It is still
              in the details modal, which is where you go when you actually want it.
            */}
            <Tooltip label={liveUrl}>
              <Anchor
                visibleFrom="sm"
                href={liveUrl}
                target="_blank"
                rel="noreferrer"
                draggable={false}
                onDragStart={(e) => e.preventDefault()}
                onClick={(e) => e.stopPropagation()}
                underline="always"
              >
                <Code>
                  <Highlight component="span" highlight={terms}>{node.pathSlug}</Highlight>
                </Code>
              </Anchor>
            </Tooltip>

            {!node.isActive && <Badge color="red" size="sm">{t('categories.inactive')}</Badge>}
            {!node.isMenu && <Badge color="blue" size="sm">{t('categories.offMenu')}</Badge>}
            {/*
              The destination is named, not just flagged: "redirects" alone would send you
              hunting through the tree to learn where. The whole tree is already here, so
              looking it up costs nothing.
            */}
            {node.redirectToId && (
              <Badge color="grape" size="sm" variant="light">
                {t('categories.redirects')} &rarr; {findNode(nodes, node.redirectToId)?.name ?? '?'}
              </Badge>
            )}
            {hasChildren && isCollapsed && (
              <Badge color="gray" variant="light" size="sm">
                {t('categories.hidden', { count: hiddenCount })}
              </Badge>
            )}
          </Group>

          {/*
            Only what has actually been filled in. Neither meta field falls back to the name,
            so a category with neither gets no block rather than a row of empty labels.
          */}
          {(node.metaTitle || node.metaDescription) && (
            <Box style={{ flex: 1, minWidth: 0 }}>
              {node.metaTitle && (
                <Text size="xs" truncate title={node.metaTitle}>
                  <Text span size="xs" c="dimmed">{t('categories.metaTitle')}&ensp;</Text>
                  {node.metaTitle}
                </Text>
              )}
              {node.metaDescription && (
                <Text size="xs" truncate title={node.metaDescription}>
                  <Text span size="xs" c="dimmed">{t('categories.metaDesc')}&ensp;</Text>
                  {node.metaDescription}
                </Text>
              )}
            </Box>
          )}

          {/* draggable={false} so pressing a button never starts a drag. */}
          <Group gap={4} wrap="nowrap" draggable={false} onDragStart={(e) => e.preventDefault()}>
            {/*
              Closes the row off: everything to the left of this describes the category,
              everything to the right does something to it. Inside this group rather than
              alongside it, because the row is laid out with space-between — a divider sitting
              as its own child would be pushed into the middle whenever a row has no meta text
              to fill the gap.
            */}
            <Divider orientation="vertical" mr="xs" />

            <Tooltip label={t('common.addChild')}>
              <ActionIcon
                variant="subtle"
                aria-label={t('common.addChild')}
                onClick={() => props.onCreate(node)}
              >
                <IconPlus size={16} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label={t('common.edit')}>
              <ActionIcon component={Link} href={`/categories/${node.id}/edit`} variant="subtle">
                <IconPencil size={16} />
              </ActionIcon>
            </Tooltip>
              <Tooltip label={t('common.history')}>
                <ActionIcon component={Link} href={`/categories/${node.id}/history`} variant="subtle">
                  <IconHistory size={16} />
                </ActionIcon>
              </Tooltip>
                {/*
                  Ordering without a drag. Precise, reachable on a phone, and the only way to
                  rearrange a list while the tree is filtered.
                */}
                <Tooltip label={t('categories.moveUp')}>
                  <ActionIcon
                    variant="subtle"
                    disabled={isFirst || !draggingAllowed}
                    aria-label={t('categories.moveUp')}
                    onClick={() => props.onStep(node, -1)}
                  >
                    <IconArrowUp size={16} />
                  </ActionIcon>
                </Tooltip>
                <Tooltip label={t('categories.moveDown')}>
                  <ActionIcon
                    variant="subtle"
                    disabled={isLast || !draggingAllowed}
                    aria-label={t('categories.moveDown')}
                    onClick={() => props.onStep(node, 1)}
                  >
                    <IconArrowDown size={16} />
                  </ActionIcon>
                </Tooltip>
                <Tooltip label={t('common.move')}>
                  <ActionIcon variant="subtle" onClick={() => props.onMove(node)}>
                    <IconArrowsMove size={16} />
                  </ActionIcon>
                </Tooltip>
                <Tooltip label={t('common.delete')}>
                  <ActionIcon variant="subtle" color="red" onClick={() => props.onDelete(node)}>
                    <IconTrash size={16} />
                  </ActionIcon>
                </Tooltip>
          </Group>
        </Group>
      </Card>

      </Group>

      {!isCollapsed &&
        children.map((child, i) => (
          <Branch
            key={child.id}
            {...props}
            node={child}
            depth={depth + 1}
            // The child gains a column of its own; whether its guide continues depends on
            // whether another sibling follows it.
            continues={[...continues, i < children.length - 1]}
          />
        ))}
    </>
  );
}

const GUIDE_WIDTH = 24;
/** Where the vertical line sits inside a column: under the chevron of the row above. */
const GUIDE_LINE = 11;

/**
 * One indent column of the tree's connector lines.
 *
 * A plain column is a vertical line, drawn only while the ancestor it belongs to still has
 * siblings to come — that is what stops a line trailing past the end of a branch. The last
 * column of a row is the elbow: it rises to meet the parent's line, turns right into the card,
 * and carries on downwards only when another sibling follows.
 *
 * Absolutely positioned inside a stretched flex cell, so the lines span the row's padding too
 * and meet the next row without a gap.
 */
function TreeGuide({
  runsOn, isElbow, colour,
}: {
  runsOn: boolean;
  isElbow: boolean;
  /** The branch's own tint, so one root's subtree is distinguishable from the next. */
  colour: string;
}) {
  const line = { position: 'absolute' as const, background: colour };

  return (
    <div style={{ width: GUIDE_WIDTH, flexShrink: 0, position: 'relative' }} aria-hidden>
      {!isElbow && runsOn && (
        <span style={{ ...line, left: GUIDE_LINE, top: 0, bottom: 0, width: 1 }} />
      )}

      {isElbow && (
        <>
          <span style={{ ...line, left: GUIDE_LINE, top: 0, height: '50%', width: 1 }} />
          <span
            style={{
              ...line,
              left: GUIDE_LINE,
              top: '50%',
              width: GUIDE_WIDTH - GUIDE_LINE,
              height: 1,
            }}
          />
          {runsOn && (
            <span style={{ ...line, left: GUIDE_LINE, top: '50%', bottom: 0, width: 1 }} />
          )}
        </>
      )}
    </div>
  );
}
