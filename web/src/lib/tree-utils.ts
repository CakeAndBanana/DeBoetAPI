import type { CategoryNode } from './categories';

/**
 * Every id beneath a node, plus the node itself.
 *
 * This is what makes a drag-and-drop move safe: dropping a category onto its own descendant
 * would detach the branch from every root, and the API rejects it with a 409. Computing the
 * set up front lets the UI refuse the drop before it is attempted, rather than explaining a
 * rejection afterwards.
 */
export function collectSubtreeIds(node: CategoryNode): Set<string> {
  const ids = new Set<string>([node.id]);

  const walk = (n: CategoryNode) => {
    for (const child of n.children ?? []) {
      ids.add(child.id);
      walk(child);
    }
  };

  walk(node);
  return ids;
}

/** Finds a node anywhere in the forest. */
export function findNode(nodes: CategoryNode[], id: string): CategoryNode | null {
  for (const node of nodes) {
    if (node.id === id) return node;
    const found = findNode(node.children ?? [], id);
    if (found) return found;
  }
  return null;
}

/**
 * Whether `draggedId` may be dropped onto `targetId`. Rejects dropping onto itself or into
 * its own subtree; a null target means "make it a root", which is always allowed.
 */
export function canDrop(nodes: CategoryNode[], draggedId: string, targetId: string | null): boolean {
  if (!draggedId) return false;
  if (targetId === null) return true;
  if (draggedId === targetId) return false;

  const dragged = findNode(nodes, draggedId);
  if (!dragged) return false;

  return !collectSubtreeIds(dragged).has(targetId);
}

/**
 * How many categories disappear when this node is collapsed — the whole subtree, not just the
 * direct children. Collapsing a parent hides its grandchildren too, so counting one level
 * would under-report what the user can no longer see.
 */
export function countDescendants(node: CategoryNode): number {
  // collectSubtreeIds includes the node itself, which is still visible when collapsed.
  return collectSubtreeIds(node).size - 1;
}

/** Splits a search box into lowercase terms, all of which must match. */
export function searchTerms(search: string): string[] {
  return search.trim().toLowerCase().split(/\s+/).filter(Boolean);
}

/** Whether one category answers the search, by name or by route. */
export function matches(node: CategoryNode, terms: string[]): boolean {
  const haystack = `${node.name} ${node.pathSlug}`.toLowerCase();
  return terms.every((term) => haystack.includes(term));
}

/**
 * The tree narrowed to a search, keeping every row that explains why a match is on screen.
 *
 * A node survives when it matches, when an ancestor matches — so searching a parent shows its
 * whole branch — or when a descendant matches, which is what keeps the path down to a deep hit
 * visible instead of showing an orphaned leaf.
 *
 * New node objects are returned with pruned children, so callers that need real tree structure
 * (drop validation, the move modal's subtree) must keep using the unfiltered tree.
 */
export function filterTree(nodes: CategoryNode[], terms: string[]): CategoryNode[] {
  if (terms.length === 0) return nodes;

  const walk = (list: CategoryNode[], underMatch: boolean): CategoryNode[] =>
    list.flatMap((node) => {
      const hit = matches(node, terms);
      const children = walk(node.children ?? [], underMatch || hit);

      return underMatch || hit || children.length > 0
        ? [{ ...node, children }]
        : [];
    });

  return walk(nodes, false);
}

/** How many categories a (possibly filtered) forest holds, counting every level. */
export function countNodes(nodes: CategoryNode[]): number {
  return nodes.reduce((total, node) => total + 1 + countNodes(node.children ?? []), 0);
}

/** The bit of the DOM the drop hit-test needs, so the logic can be exercised without one. */
export interface Hit {
  closest(selector: string): {
    getAttribute(name: string): string | null;
    getBoundingClientRect?(): { top: number; height: number };
  } | null;
}

export interface DropTarget {
  /** The category being dropped onto; null means "make it a root". */
  id: string | null;
  /** Whether the move is allowed, and so whether to highlight and accept it. */
  valid: boolean;
  /** Whether this lands inside the target or beside it. Always inside for the root zone. */
  mode: DropMode;
}

/**
 * Turns whatever sits under the pointer into a drop decision.
 *
 * Separated from the tree component because it is the half of dragging that can be wrong
 * silently: a renamed data attribute or a missed `closest` would simply stop every drop
 * working, with nothing to show for it.
 */
export function dropTargetFrom(
  element: Hit | null,
  nodes: CategoryNode[],
  sourceId: string | null,
  y?: number,
): DropTarget {
  if (!element || !sourceId) return { id: null, valid: false, mode: 'inside' };

  // The root zone wins: it sits above the tree while a drag is in progress.
  if (element.closest('[data-root-drop]')) return { id: null, valid: true, mode: 'inside' };

  const card = element.closest('[data-category-id]');
  const id = card?.getAttribute('data-category-id') ?? null;

  const rect = y === undefined ? undefined : card?.getBoundingClientRect?.();
  const mode: DropMode = rect && y !== undefined
    ? dropModeFor(rect.top, rect.height, y)
    : 'inside';

  // Beside a category is a reorder, and reordering next to your own descendant is fine —
  // only landing *inside* your own subtree would detach the branch.
  const valid = !!id && (mode === 'inside'
    ? canDrop(nodes, sourceId, id)
    : id !== sourceId && !!placementFor(nodes, sourceId, id, mode));

  return { id, valid, mode };
}

/** Where within a card a pointer landed, and so what dropping there means. */
export type DropMode = 'before' | 'inside' | 'after';

/**
 * Reads a drop position out of where the pointer sits inside a card.
 *
 * The middle means "put it inside this one"; the top and bottom edges mean "put it above or
 * below this one, as a sibling". The edge band is capped so it stays a band rather than
 * swallowing a short row whole, and stays reachable on a tall one.
 */
export function dropModeFor(top: number, height: number, y: number): DropMode {
  const edge = Math.min(10, height / 4);

  if (y < top + edge) return 'before';
  if (y > top + height - edge) return 'after';
  return 'inside';
}

export interface Placement {
  /** The parent to sit under; null for a root category. */
  parentId: string | null;
  /** The index to take among that parent's children, once the moved node is taken out. */
  position: number;
}

/** A node's parent and the siblings it sits among. */
function siblingsOf(
  nodes: CategoryNode[],
  id: string,
): { parentId: string | null; siblings: CategoryNode[] } | null {
  const walk = (
    list: CategoryNode[],
    parentId: string | null,
  ): { parentId: string | null; siblings: CategoryNode[] } | null => {
    if (list.some((n) => n.id === id)) return { parentId, siblings: list };

    for (const node of list) {
      const found = walk(node.children ?? [], node.id);
      if (found) return found;
    }

    return null;
  };

  return walk(nodes, null);
}

/** Where a category sits among its siblings, for the up and down controls. */
export function indexOf(nodes: CategoryNode[], id: string): { index: number; count: number } | null {
  const found = siblingsOf(nodes, id);
  if (!found) return null;

  return { index: found.siblings.findIndex((n) => n.id === id), count: found.siblings.length };
}

/**
 * Turns "drop this above that one" into the parent and index the API wants.
 *
 * The index is counted with the dragged category already removed, because that is how the
 * server places it — inserting into the list it left. Counting against the list it is still in
 * would put it one place too far whenever it moves downwards among its own siblings.
 */
export function placementFor(
  nodes: CategoryNode[],
  sourceId: string,
  targetId: string,
  mode: 'before' | 'after',
): Placement | null {
  const found = siblingsOf(nodes, targetId);
  if (!found || sourceId === targetId) return null;

  const remaining = found.siblings.filter((n) => n.id !== sourceId);
  const target = remaining.findIndex((n) => n.id === targetId);
  if (target < 0) return null;

  return { parentId: found.parentId, position: mode === 'before' ? target : target + 1 };
}

/** The parent and index for a step up or down among a category's own siblings. */
export function stepFor(nodes: CategoryNode[], id: string, delta: -1 | 1): Placement | null {
  const found = siblingsOf(nodes, id);
  const at = indexOf(nodes, id);
  if (!found || !at || at.index < 0) return null;

  const position = at.index + delta;
  if (position < 0 || position >= at.count) return null;

  return { parentId: found.parentId, position };
}

/**
 * Hues for the branch tints, chosen rather than generated.
 *
 * A hue straight off a hash lands anywhere, including the muddy yellow-greens and the muted
 * browns that look like a rendering fault. Ten spaced, clean hues stay distinguishable from
 * each other at a glance and pleasant at low saturation, which is the whole point of the
 * effect: enough colour to tell branches apart, not enough to compete with the content.
 */
const BRANCH_HUES = [212, 145, 32, 280, 350, 190, 95, 318, 58, 255];

/**
 * A stable hue for a branch, derived from its root category's id.
 *
 * Derived rather than stored, so it costs no schema, no migration and no decision when a
 * category is created — and it never drifts, because an id never changes. A rename or a
 * reorder leaves the colour alone; only moving a category to a different root re-tints it,
 * which is exactly the moment its branch really did change.
 */
export function branchHue(id: string): number {
  let hash = 0;

  for (let i = 0; i < id.length; i += 1) {
    // The time-ordered prefix of a v7 uuid is nearly identical between siblings created
    // together, so every character has to feed the hash for the tail to break the tie.
    hash = (Math.imul(hash, 31) + id.charCodeAt(i)) >>> 0;
  }

  return BRANCH_HUES[hash % BRANCH_HUES.length];
}

/**
 * The tint itself, as a CSS colour.
 *
 * Semi-transparent so it settles onto whatever is behind it: the same value reads as a soft
 * pastel on a light background and a muted line on a dark one, with no second palette and no
 * theme-dependent branch.
 */
export function branchColour(id: string): string {
  return `hsl(${branchHue(id)} 60% 52% / 0.55)`;
}

/**
 * A colour per root, each one different from the others.
 *
 * The id alone is not enough: ten hues and a handful of roots collide often — in testing,
 * three roots was enough to give two of them the same tint, which defeats the point. So the
 * hue derived from the id is a *preference*, and a root whose preference is already taken
 * steps to the next free one.
 *
 * Order matters, and it is the tree's own order, so the result is the same on every render and
 * for every viewer. A root keeps its colour as long as no new root ahead of it wants the same
 * one; beyond `BRANCH_HUES.length` roots the palette repeats, because more distinguishable
 * soft hues than that do not exist.
 */
export function branchColours(rootIds: string[]): Map<string, string> {
  const taken = new Set<number>();
  const colours = new Map<string, string>();

  for (const id of rootIds) {
    const preferred = branchHue(id);
    let hue = preferred;

    if (taken.has(preferred) && taken.size < BRANCH_HUES.length) {
      const from = BRANCH_HUES.indexOf(preferred);
      for (let step = 1; step < BRANCH_HUES.length; step += 1) {
        const candidate = BRANCH_HUES[(from + step) % BRANCH_HUES.length];
        if (!taken.has(candidate)) {
          hue = candidate;
          break;
        }
      }
    }

    taken.add(hue);
    colours.set(id, `hsl(${hue} 60% 52% / 0.55)`);
  }

  return colours;
}
