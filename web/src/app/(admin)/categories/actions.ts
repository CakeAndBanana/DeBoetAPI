'use server';

import { revalidatePath } from 'next/cache';
import { apiFetch } from '@/lib/api';
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

export async function moveCategory(id: string, newParentId: string | null) {
  return await run(() =>
    apiFetch(`/api/v1/categories/${id}/move`, {
      method: 'POST',
      body: JSON.stringify({ newParentId }),
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
