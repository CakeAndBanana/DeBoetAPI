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
