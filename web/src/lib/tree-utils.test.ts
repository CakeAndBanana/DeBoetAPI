import { describe, expect, it } from 'vitest';
import type { CategoryNode } from './categories';
import {
  canDrop, collectSubtreeIds, countDescendants, countNodes, dropTargetFrom, filterTree,
  findNode, matches, searchTerms,
} from './tree-utils';

/** A node with only the fields the tree helpers actually read. */
function node(name: string, pathSlug: string, children: CategoryNode[] = []): CategoryNode {
  return {
    id: name,
    name,
    slug: name.toLowerCase().replaceAll(' ', ''),
    pathSlug,
    metaTitle: null,
    metaDescription: null,
    depth: 0,
    sortOrder: 0,
    isActive: true,
    isMenu: true,
    children,
  };
}

/**
 * Christmas
 *   Christmas Lights
 *     Ice Lights
 *     Fairy Lights
 *   Trees
 * Halloween
 *   Pumpkins
 *   Lights
 */
const tree: CategoryNode[] = [
  node('Christmas', 'christmas', [
    node('Christmas Lights', 'christmas-christmaslights', [
      node('Ice Lights', 'christmas-christmaslights-icelights'),
      node('Fairy Lights', 'christmas-christmaslights-fairylights'),
    ]),
    node('Trees', 'christmas-trees'),
  ]),
  node('Halloween', 'halloween', [
    node('Pumpkins', 'halloween-pumpkins'),
    node('Lights', 'halloween-lights'),
  ]),
];

/** The visible shape of a forest, indented, so a filter's result reads at a glance. */
function shape(nodes: CategoryNode[], depth = 0): string[] {
  return nodes.flatMap((n) => [
    '  '.repeat(depth) + n.name,
    ...shape(n.children ?? [], depth + 1),
  ]);
}

describe('searchTerms', () => {
  it('splits on whitespace and lowercases', () => {
    expect(searchTerms('  Ice   LIGHTS ')).toEqual(['ice', 'lights']);
  });

  it('treats a blank box as no search at all', () => {
    expect(searchTerms('')).toEqual([]);
    expect(searchTerms('   ')).toEqual([]);
  });
});

describe('matches', () => {
  const christmas = tree[0];

  it('reads the name', () => {
    expect(matches(christmas, searchTerms('christmas'))).toBe(true);
  });

  it('reads the route as well, so a hit can be found by its address', () => {
    expect(matches(tree[1].children[0], searchTerms('halloween-pump'))).toBe(true);
  });

  it('requires every term, not any of them', () => {
    expect(matches(christmas, searchTerms('christmas halloween'))).toBe(false);
  });
});

describe('filterTree', () => {
  it('keeps the path down to a deep hit rather than orphaning it', () => {
    expect(shape(filterTree(tree, searchTerms('ice')))).toEqual([
      'Christmas',
      '  Christmas Lights',
      '    Ice Lights',
    ]);
  });

  it('keeps a matched branch whole, children included', () => {
    expect(shape(filterTree(tree, searchTerms('christmas lights')))).toEqual([
      'Christmas',
      '  Christmas Lights',
      '    Ice Lights',
      '    Fairy Lights',
    ]);
  });

  it('follows a term into every branch it appears in', () => {
    expect(shape(filterTree(tree, searchTerms('lights')))).toEqual([
      'Christmas',
      '  Christmas Lights',
      '    Ice Lights',
      '    Fairy Lights',
      'Halloween',
      '  Lights',
    ]);
  });

  it('returns the tree untouched when there is no search', () => {
    expect(filterTree(tree, searchTerms('   '))).toBe(tree);
  });

  it('returns nothing when nothing matches', () => {
    expect(filterTree(tree, searchTerms('zzz'))).toEqual([]);
  });

  it('is case insensitive', () => {
    expect(shape(filterTree(tree, searchTerms('ICE')))).toEqual(
      shape(filterTree(tree, searchTerms('ice'))),
    );
  });

  it('leaves the source tree alone', () => {
    filterTree(tree, searchTerms('ice'));
    expect(countNodes(tree)).toBe(8);
    expect(tree[0].children).toHaveLength(2);
  });
});

describe('collectSubtreeIds', () => {
  it('includes the node itself along with every descendant', () => {
    expect([...collectSubtreeIds(tree[0])].sort()).toEqual([
      'Christmas', 'Christmas Lights', 'Fairy Lights', 'Ice Lights', 'Trees',
    ]);
  });

  it('is just the node when it has no children', () => {
    expect([...collectSubtreeIds(tree[1].children[0])]).toEqual(['Pumpkins']);
  });
});

describe('countDescendants', () => {
  it('counts the whole subtree, not one level', () => {
    // Collapsing Christmas hides its grandchildren too, so counting children under-reports.
    expect(countDescendants(tree[0])).toBe(4);
  });

  it('is zero for a leaf', () => {
    expect(countDescendants(tree[1].children[0])).toBe(0);
  });
});

describe('countNodes', () => {
  it('counts every level of a forest', () => {
    expect(countNodes(tree)).toBe(8);
  });

  it('is zero for an empty forest', () => {
    expect(countNodes([])).toBe(0);
  });
});

describe('findNode', () => {
  it('finds a node at any depth', () => {
    expect(findNode(tree, 'Ice Lights')?.name).toBe('Ice Lights');
  });

  it('returns null for an id that is not there', () => {
    expect(findNode(tree, 'nope')).toBeNull();
  });
});

describe('canDrop', () => {
  it('allows a move into an unrelated branch', () => {
    expect(canDrop(tree, 'Ice Lights', 'Halloween')).toBe(true);
  });

  it('allows detaching to the top level', () => {
    expect(canDrop(tree, 'Ice Lights', null)).toBe(true);
  });

  it('refuses dropping a node onto itself', () => {
    expect(canDrop(tree, 'Christmas', 'Christmas')).toBe(false);
  });

  it('refuses dropping a node into its own child', () => {
    expect(canDrop(tree, 'Christmas', 'Christmas Lights')).toBe(false);
  });

  it('refuses dropping a node into a deeper descendant', () => {
    // The case the API answers with a 409: the branch would detach from every root.
    expect(canDrop(tree, 'Christmas', 'Ice Lights')).toBe(false);
  });

  it('allows a child to move up to its own grandparent', () => {
    expect(canDrop(tree, 'Ice Lights', 'Christmas')).toBe(true);
  });

  it('refuses a source that is not in the tree', () => {
    expect(canDrop(tree, 'ghost', 'Christmas')).toBe(false);
    expect(canDrop(tree, '', 'Christmas')).toBe(false);
  });
});

/** Stands in for a DOM element, so the hit-test can be exercised without a browser. */
function hit(chain: Record<string, string | null>) {
  return {
    closest(selector: string) {
      if (!(selector in chain)) return null;
      const value = chain[selector];
      return { getAttribute: () => value };
    },
  };
}

describe('dropTargetFrom', () => {
  it('reads the category under the pointer', () => {
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Halloween' }), tree, 'Ice Lights'))
      .toEqual({ id: 'Halloween', valid: true });
  });

  it('treats the root zone as a valid drop, whatever is behind it', () => {
    const overRoot = hit({ '[data-root-drop]': '', '[data-category-id]': 'Christmas' });

    expect(dropTargetFrom(overRoot, tree, 'Ice Lights')).toEqual({ id: null, valid: true });
  });

  it('marks a drop into the dragged node’s own subtree invalid', () => {
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Ice Lights' }), tree, 'Christmas'))
      .toEqual({ id: 'Ice Lights', valid: false });
  });

  it('is not a target when the pointer is over nothing in the tree', () => {
    expect(dropTargetFrom(hit({}), tree, 'Ice Lights')).toEqual({ id: null, valid: false });
  });

  it('is not a target when no drag is in progress', () => {
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Halloween' }), tree, null))
      .toEqual({ id: null, valid: false });
  });

  it('survives elementFromPoint returning null, which it does past the viewport edge', () => {
    expect(dropTargetFrom(null, tree, 'Ice Lights')).toEqual({ id: null, valid: false });
  });
});
