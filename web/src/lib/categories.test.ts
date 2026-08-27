import { describe, expect, it } from 'vitest';
import { flatten, type CategoryNode } from './categories';

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
