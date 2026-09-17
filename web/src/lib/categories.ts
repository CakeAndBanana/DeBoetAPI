import { apiFetch } from './api';
import type { components } from './api-types';

export type Category = components['schemas']['CategoryResponse'];
export type CategoryNode = components['schemas']['CategoryNode'];
export type CategoryDetail = components['schemas']['CategoryDetailResponse'];
export type AuditEntry = components['schemas']['AuditEntryResponse'];

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** The admin view deliberately includes inactive and off-menu categories. */
export function getTree() {
  return apiFetch<CategoryNode[]>('/api/v1/categories/tree?includeInactive=true');
}

export function getCategory(idOrSlug: string) {
  return apiFetch<CategoryDetail>(`/api/v1/categories/${idOrSlug}`);
}

export function listCategories(pageSize = 200) {
  return apiFetch<Paged<Category>>(
    `/api/v1/categories?includeInactive=true&pageSize=${pageSize}`,
  );
}

/** One category's history. Paged like the main feed: a busy category outgrows one screen. */
export function getCategoryHistory(id: string, page?: number) {
  const params = new URLSearchParams({ pageSize: String(AUDIT_PAGE_SIZE) });
  if (page) params.set('page', String(page));

  return apiFetch<Paged<AuditEntry>>(`/api/v1/categories/${id}/history?${params}`);
}

/**
 * Rows per page of history.
 *
 * Enough that paging is rare in practice, and far short of loading a whole table: ten thousand
 * changes in one response would be slow to fetch, slow to render and useless to read.
 */
export const AUDIT_PAGE_SIZE = 250;

export interface AuditFilters {
  entityType?: string;
  action?: string;
  changedById?: string;
  page?: number;
}

export function getAudit(filters: AuditFilters = {}) {
  const params = new URLSearchParams({ pageSize: String(AUDIT_PAGE_SIZE) });
  if (filters.entityType) params.set('entityType', filters.entityType);
  if (filters.action) params.set('action', filters.action);
  if (filters.changedById) params.set('changedById', filters.changedById);
  if (filters.page) params.set('page', String(filters.page));

  return apiFetch<Paged<AuditEntry>>(`/api/v1/audit?${params}`);
}

export interface CategoryOption {
  value: string;
  label: string;
  /** The name without the indent the label carries, for naming one category in a sentence. */
  name: string;
  /** The full route, carried so the picker can search and disambiguate by it. */
  pathSlug: string;
  /** The main parent, so the chain above an option can be walked without asking the API. */
  parentId: string | null;
  isActive: boolean;
}

/**
 * Flattens the tree for parent pickers, indenting names by depth.
 *
 * The route travels with each option because names repeat across branches — several
 * “Lights” under different parents are indistinguishable by name alone once the list is
 * long enough to need searching.
 */
export function flatten(
  nodes: CategoryNode[],
  depth = 0,
  parentId: string | null = null,
): CategoryOption[] {
  return nodes.flatMap((node) => [
    {
      value: node.id,
      label: `${'— '.repeat(depth)}${node.name}`,
      name: node.name,
      pathSlug: node.pathSlug,
      parentId,
      isActive: node.isActive,
    },
    ...flatten(node.children ?? [], depth + 1, node.id),
  ]);
}

/**
 * The nearest switched-off category at or above this one, or null when the whole chain is on.
 *
 * The API will not have a category active while anything above it is inactive, so this is what
 * the form asks before it offers the switch at all — walking the parents the picker already
 * holds rather than making a request to find out.
 */
export function inactiveAbove(
  options: CategoryOption[],
  startId: string | null | undefined,
): CategoryOption | null {
  const byId = new Map(options.map((o) => [o.value, o]));
  const seen = new Set<string>();

  let at = startId ?? null;

  while (at !== null && !seen.has(at)) {
    seen.add(at);

    const option = byId.get(at);
    if (!option) return null;
    if (!option.isActive) return option;

    at = option.parentId;
  }

  return null;
}
