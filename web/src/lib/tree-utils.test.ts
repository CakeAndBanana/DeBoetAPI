import { describe, expect, it } from 'vitest';
import type { CategoryNode } from './categories';
import {
  canDrop, collectSubtreeIds, countDescendants, countNodes, dropModeFor, dropTargetFrom,
  filterTree, findNode, indexOf, matches, placementFor, searchTerms, stepFor,
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

/**
 * Stands in for a DOM element, so the hit-test can be exercised without a browser.
 * `rect` gives the matched card a height, which is what the before/inside/after split reads.
 */
function hit(chain: Record<string, string | null>, rect = { top: 100, height: 60 }) {
  return {
    closest(selector: string) {
      if (!(selector in chain)) return null;
      const value = chain[selector];
      return { getAttribute: () => value, getBoundingClientRect: () => rect };
    },
  };
}

describe('dropTargetFrom', () => {
  it('reads the category under the pointer', () => {
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Halloween' }), tree, 'Ice Lights'))
      .toEqual({ id: 'Halloween', valid: true, mode: 'inside' });
  });

  it('treats the root zone as a valid drop, whatever is behind it', () => {
    const overRoot = hit({ '[data-root-drop]': '', '[data-category-id]': 'Christmas' });

    expect(dropTargetFrom(overRoot, tree, 'Ice Lights'))
      .toEqual({ id: null, valid: true, mode: 'inside' });
  });

  it('marks a drop into the dragged node’s own subtree invalid', () => {
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Ice Lights' }), tree, 'Christmas'))
      .toEqual({ id: 'Ice Lights', valid: false, mode: 'inside' });
  });

  it('is not a target when the pointer is over nothing in the tree', () => {
    expect(dropTargetFrom(hit({}), tree, 'Ice Lights'))
      .toEqual({ id: null, valid: false, mode: 'inside' });
  });

  it('refuses the root zone too when no drag is in progress', () => {
    const overRoot = hit({ '[data-root-drop]': '' });

    expect(dropTargetFrom(overRoot, tree, null)).toEqual({ id: null, valid: false, mode: 'inside' });
  });

  it('is not a target when no drag is in progress', () => {
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Halloween' }), tree, null))
      .toEqual({ id: null, valid: false, mode: 'inside' });
  });

  it('survives elementFromPoint returning null, which it does past the viewport edge', () => {
    expect(dropTargetFrom(null, tree, 'Ice Lights')).toEqual({ id: null, valid: false, mode: 'inside' });

  });

  it('reads the top edge of a card as a reorder above it', () => {
    // y=103 on a card spanning 100..160 is inside the 10px top band.
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Trees' }), tree, 'Pumpkins', 103))
      .toEqual({ id: 'Trees', valid: true, mode: 'before' });
  });

  it('reads the bottom edge as a reorder below it', () => {
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Trees' }), tree, 'Pumpkins', 157))
      .toEqual({ id: 'Trees', valid: true, mode: 'after' });
  });

  it('reads the middle as making it a child', () => {
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Trees' }), tree, 'Pumpkins', 130))
      .toEqual({ id: 'Trees', valid: true, mode: 'inside' });
  });

  it('allows reordering beside your own descendant, which is not a cycle', () => {
    // Dropping Christmas *inside* Ice Lights would detach the branch; dropping it beside Ice
    // Lights only changes who sits where in that list, so it is allowed.
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Ice Lights' }), tree, 'Christmas', 103))
      .toEqual({ id: 'Ice Lights', valid: true, mode: 'before' });
  });

  it('refuses a drop beside itself', () => {
    expect(dropTargetFrom(hit({ '[data-category-id]': 'Trees' }), tree, 'Trees', 103))
      .toEqual({ id: 'Trees', valid: false, mode: 'before' });
  });
});

describe('dropModeFor', () => {
  // A 60px card starting at y=100: edges are the top and bottom 10px.
  it('reads the top edge as "put it above"', () => {
    expect(dropModeFor(100, 60, 103)).toBe('before');
  });

  it('reads the bottom edge as "put it below"', () => {
    expect(dropModeFor(100, 60, 157)).toBe('after');
  });

  it('reads the middle as "put it inside"', () => {
    expect(dropModeFor(100, 60, 130)).toBe('inside');
  });

  it('keeps a middle to aim at on a short card', () => {
    // A quarter each way rather than 10px, so a 20px row is not all edge.
    expect(dropModeFor(0, 20, 10)).toBe('inside');
    expect(dropModeFor(0, 20, 1)).toBe('before');
    expect(dropModeFor(0, 20, 19)).toBe('after');
  });
});

describe('indexOf', () => {
  it('finds a root among the roots', () => {
    expect(indexOf(tree, 'Halloween')).toEqual({ index: 1, count: 2 });
  });

  it('finds a child among its siblings, not among everything', () => {
    expect(indexOf(tree, 'Trees')).toEqual({ index: 1, count: 2 });
  });

  it('is null for an unknown id', () => {
    expect(indexOf(tree, 'nope')).toBeNull();
  });
});

describe('stepFor', () => {
  it('steps a root up', () => {
    expect(stepFor(tree, 'Halloween', -1)).toEqual({ parentId: null, position: 0 });
  });

  it('steps a child down within its own parent', () => {
    expect(stepFor(tree, 'Christmas Lights', 1)).toEqual({ parentId: 'Christmas', position: 1 });
  });

  it('refuses to step the first one up', () => {
    expect(stepFor(tree, 'Christmas', -1)).toBeNull();
  });

  it('refuses to step the last one down', () => {
    expect(stepFor(tree, 'Trees', 1)).toBeNull();
  });
});

describe('placementFor', () => {
  it('places before a target in another branch', () => {
    expect(placementFor(tree, 'Trees', 'Lights', 'before'))
      .toEqual({ parentId: 'Halloween', position: 1 });
  });

  it('places after a target in another branch', () => {
    expect(placementFor(tree, 'Trees', 'Pumpkins', 'after'))
      .toEqual({ parentId: 'Halloween', position: 1 });
  });

  it('counts with the dragged category already removed when it moves down its own list', () => {
    // Ice Lights is at 0 and Fairy Lights at 1. Dropping Ice below Fairy means index 1 once
    // Ice is out of the list — counting against the original list would say 2 and overshoot.
    expect(placementFor(tree, 'Ice Lights', 'Fairy Lights', 'after'))
      .toEqual({ parentId: 'Christmas Lights', position: 1 });
  });

  it('counts the same way moving up its own list', () => {
    expect(placementFor(tree, 'Fairy Lights', 'Ice Lights', 'before'))
      .toEqual({ parentId: 'Christmas Lights', position: 0 });
  });

  it('is null when dropped on itself', () => {
    expect(placementFor(tree, 'Trees', 'Trees', 'before')).toBeNull();
  });

  it('is null for a target that is not in the tree', () => {
    expect(placementFor(tree, 'Trees', 'nope', 'after')).toBeNull();
  });
});
