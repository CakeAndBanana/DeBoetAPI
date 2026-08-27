'use server';

import { revalidatePath } from 'next/cache';
import { apiFetch } from '@/lib/api';
import { getCategory, getCategoryHistory, type AuditEntry, type Paged } from '@/lib/categories';
import { ApiError } from '@/lib/problem';

export interface ActionResult {
  ok: boolean;
  message?: string;
  /** Ids of children promoted by a delete, so the UI can say what happened to them. */
  promoted?: string[];
}

/**
 * Server Actions rather than client fetches: the bearer token stays on the server, and unlike
 * a Server Component render these may write cookies if a refresh becomes necessary.
 */
async function run(fn: () => Promise<unknown>): Promise<ActionResult> {
  try {
    const result = await fn();
    revalidatePath('/categories');
    revalidatePath('/audit');
    return { ok: true, ...(result as object) };
  } catch (error) {
    // The API's own wording is already precise ("That would place the category inside its own
    // subtree."), so it is passed through rather than replaced.
    if (error instanceof ApiError) return { ok: false, message: error.message };
    return { ok: false, message: 'Something went wrong talking to the API.' };
  }
}

export interface CategoryInput {
  name: string;
  slug?: string;
  /** Another category this one forwards to, or null for an ordinary category. */
  redirectToId?: string | null;
  metaTitle?: string;
  metaDescription?: string;
  parentId?: string | null;
  sortOrder: number;
  isActive: boolean;
  isMenu: boolean;
}

export async function createCategory(input: CategoryInput) {
  return await run(() =>
    apiFetch('/api/v1/categories', { method: 'POST', body: JSON.stringify(input) }),
  );
}

export async function updateCategory(id: string, input: CategoryInput) {
  return await run(() =>
    apiFetch(`/api/v1/categories/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  );
}

/**
  * Reparents a category, repositions it among its siblings, or both.
  *
  * One call rather than two, because dragging a category between two others under a different
  * parent is both at once, and splitting it would leave the tree briefly wrong.
  */
export async function moveCategory(
  id: string,
  newParentId: string | null,
  position?: number,
) {
  return await run(() =>
    apiFetch(`/api/v1/categories/${id}/move`, {
      method: 'POST',
      body: JSON.stringify({ newParentId, position }),
    }),
  );
}

export async function deleteCategory(id: string) {
  return await run(async () => {
    const result = await apiFetch<{ promotedChildIds: string[] }>(`/api/v1/categories/${id}`, {
      method: 'DELETE',
    });
    return { promoted: result?.promotedChildIds ?? [] };
  });
}

export async function addLink(childId: string, parentId: string) {
  return await run(() =>
    apiFetch(`/api/v1/categories/${childId}/links`, {
      method: 'POST',
      body: JSON.stringify({ parentId }),
    }),
  );
}

export async function removeLink(childId: string, parentId: string) {
  return await run(() =>
    apiFetch(`/api/v1/categories/${childId}/links/${parentId}`, { method: 'DELETE' }),
  );
}

export interface CategoryDetails {
  id: string;
  name: string;
  slug: string;
  pathSlug: string;
  /** Ancestors then the category itself, so the modal can print Christmas › Ice Lights. */
  trail: { id: string; name: string }[];
  metaTitle: string | null;
  metaDescription: string | null;
  isActive: boolean;
  isMenu: boolean;
  /** Where this category forwards to, resolved, or null when it does not. */
  redirectTo: { id: string; name: string; pathSlug: string } | null;
  childCount: number;
  secondaryParents: { id: string; name: string; pathSlug: string }[];
  createdAt: string;
  /**
   * When the category was last touched, taken from the audit trail so it always refers to the
   * same event as `updatedBy`. Falls back to the row's own timestamp when there is no trail to
   * read — which also means it can differ from it: adding a secondary link is recorded as a
   * change to this category but writes a row in `category_links`, leaving `updated_at` alone.
   */
  updatedAt: string;
  createdBy: string | null;
  updatedBy: string | null;
  lastAction: string | null;
  lastSummary: string | null;
  /** Null when the account may not read the trail, which is what carries the names. */
  changeCount: number | null;
}

/**
 * Everything the details modal shows, in one round trip from the browser's point of view.
 *
 * Two API calls because the names live in the audit trail: `categories` stores only
 * `created_by_id` / `updated_by_id`, and turning those into names would mean reading
 * `/users`, which editors may not. The trail already snapshots `changed_by_name` at write
 * time, so it answers "who" without any extra permission — and keeps reading correctly after
 * an account is disabled.
 */
export async function categoryDetails(
  id: string,
): Promise<{ ok: true; details: CategoryDetails } | { ok: false; message: string }> {
  try {
    const detail = await getCategory(id);

    let history: Paged<AuditEntry> | null = null;
    try {
      history = await getCategoryHistory(id);
    } catch (error) {
      // A 403 here just means no trail for this account. Anything else — including the
      // redirect Next throws on an expired session — has to keep propagating.
      if (!(error instanceof ApiError)) throw error;
    }

    const entries = history?.items ?? [];
    // Newest first, so [0] is the most recent change. The Created entry is the oldest and may
    // fall outside the fetched page on a heavily edited category; the date is shown regardless.
    const created = entries.find((e) => e.action === 'Created');

    return {
      ok: true,
      details: {
        id: detail.category.id,
        name: detail.category.name,
        slug: detail.category.slug,
        pathSlug: detail.category.pathSlug,
        trail: [...detail.breadcrumb, detail.category].map((c) => ({ id: c.id, name: c.name })),
        metaTitle: detail.category.metaTitle,
        metaDescription: detail.category.metaDescription,
        isActive: detail.category.isActive,
        isMenu: detail.category.isMenu,
        redirectTo: detail.redirectTo
          ? {
              id: detail.redirectTo.id,
              name: detail.redirectTo.name,
              pathSlug: detail.redirectTo.pathSlug,
            }
          : null,
        childCount: detail.children.length,
        secondaryParents: detail.secondaryParents.map((c) => ({
          id: c.id,
          name: c.name,
          pathSlug: c.pathSlug,
        })),
        createdAt: detail.category.createdAt,
        updatedAt: entries[0]?.changedAt ?? detail.category.updatedAt,
        createdBy: created?.changedByName ?? null,
        updatedBy: entries[0]?.changedByName ?? null,
        lastAction: entries[0]?.action ?? null,
        lastSummary: entries[0]?.summary ?? null,
        changeCount: history?.totalCount ?? null,
      },
    };
  } catch (error) {
    if (error instanceof ApiError) return { ok: false, message: error.message };
    throw error;
  }
}
