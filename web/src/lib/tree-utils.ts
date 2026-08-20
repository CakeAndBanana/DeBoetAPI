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
