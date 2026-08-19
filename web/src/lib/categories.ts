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

export function getCategoryHistory(id: string) {
  return apiFetch<Paged<AuditEntry>>(`/api/v1/categories/${id}/history?pageSize=100`);
}

export interface AuditFilters {
  entityType?: string;
  changedById?: string;
  page?: number;
}

export function getAudit(filters: AuditFilters = {}) {
  const params = new URLSearchParams({ pageSize: '50' });
  if (filters.entityType) params.set('entityType', filters.entityType);
  if (filters.changedById) params.set('changedById', filters.changedById);
  if (filters.page) params.set('page', String(filters.page));

  return apiFetch<Paged<AuditEntry>>(`/api/v1/audit?${params}`);
}

/** Flattens the tree for parent pickers, indenting names by depth. */
export function flatten(nodes: CategoryNode[], depth = 0): { value: string; label: string }[] {
  return nodes.flatMap((node) => [
    { value: node.id, label: `${'— '.repeat(depth)}${node.name}` },
    ...flatten(node.children ?? [], depth + 1),
  ]);
}
