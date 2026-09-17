import { describe, expect, it } from 'vitest';
import { flatten, inactiveAbove, type CategoryNode } from './categories';

function node(name: string, pathSlug: string, children: CategoryNode[] = []): CategoryNode {
  return {
    id: `id-${name}`,
    name,
    slug: name.toLowerCase().replaceAll(' ', ''),
    pathSlug,
    metaTitle: null,
    metaDescription: null,
    redirectToId: null,
    depth: 0,
    sortOrder: 0,
    isActive: true,
    isMenu: true,
    children,
  };
}

const tree: CategoryNode[] = [
  node('Christmas', 'christmas', [
    node('Christmas Lights', 'christmas-christmaslights', [
      node('Ice Lights', 'christmas-christmaslights-icelights'),
    ]),
  ]),
  node('Halloween', 'halloween'),
];

/** The same node, switched off. */
function off(target: CategoryNode): CategoryNode {
  return { ...target, isActive: false };
}

describe('flatten', () => {
  it('walks the whole tree depth first', () => {
    expect(flatten(tree).map((o) => o.value)).toEqual([
      'id-Christmas', 'id-Christmas Lights', 'id-Ice Lights', 'id-Halloween',
    ]);
  });

  it('indents by depth, so the picker still reads as a tree', () => {
    expect(flatten(tree).map((o) => o.label)).toEqual([
      'Christmas',
      '— Christmas Lights',
      '— — Ice Lights',
      'Halloween',
    ]);
  });

  it('carries the route on every option', () => {
    // Names repeat across branches; the route is what tells two "Lights" apart, and what the
    // picker searches on besides the name.
    expect(flatten(tree).map((o) => o.pathSlug)).toEqual([
      'christmas',
      'christmas-christmaslights',
      'christmas-christmaslights-icelights',
      'halloween',
    ]);
  });

  it('is empty for an empty tree', () => {
    expect(flatten([])).toEqual([]);
  });
});

/**
 * What the form asks before it offers the Active switch. The API will not have a category active
 * while anything above it is off, so the answer decides whether the switch is offered at all.
 */
describe('inactiveAbove', () => {
  it('finds nothing above a chain that is entirely on', () => {
    expect(inactiveAbove(flatten(tree), 'id-Ice Lights')).toBeNull();
  });

  it('finds the parent itself, which is what picking one in the form asks about', () => {
    const options = flatten([off(node('Christmas', 'christmas'))]);

    expect(inactiveAbove(options, 'id-Christmas')?.name).toBe('Christmas');
  });

  it('finds one several levels up, since the switch may be nowhere near', () => {
    const options = flatten([
      off(node('Christmas', 'christmas', [
        node('Christmas Lights', 'christmas-christmaslights', [
          node('Ice Lights', 'christmas-christmaslights-icelights'),
        ]),
      ])),
    ]);

    expect(inactiveAbove(options, 'id-Ice Lights')?.name).toBe('Christmas');
  });

  it('names the nearest one, because that is the switch to find first', () => {
    const options = flatten([
      off(node('Christmas', 'christmas', [
        off(node('Christmas Lights', 'christmas-christmaslights', [
          node('Ice Lights', 'christmas-christmaslights-icelights'),
        ])),
      ])),
    ]);

    expect(inactiveAbove(options, 'id-Ice Lights')?.name).toBe('Christmas Lights');
  });

  it('is nothing for a root, which has nothing above it', () => {
    expect(inactiveAbove(flatten(tree), null)).toBeNull();
  });

  it('is nothing for an id the tree does not hold', () => {
    expect(inactiveAbove(flatten(tree), 'id-Gone')).toBeNull();
  });
});
