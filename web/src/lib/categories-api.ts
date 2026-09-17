/**
 * Every read this app makes against the categories and audit endpoints.
 *
 * Split from `categories.ts` because `apiFetch` reaches for the session in `next/headers`, which
 * only exists on the server: a Client Component that imported so much as one helper from the same
 * file would drag that into the browser bundle and fail the build. Everything left next door is
 * pure, so the name most of the app imports is the one that is safe to import from anywhere.
 */
import { apiFetch } from './api';
import type { AuditEntry, Category, CategoryDetail, CategoryNode, Paged } from './categories';

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
