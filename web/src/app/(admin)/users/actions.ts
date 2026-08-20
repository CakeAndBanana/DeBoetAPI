'use server';

import { revalidatePath } from 'next/cache';
import { apiFetch } from '@/lib/api';
import { ApiError } from '@/lib/problem';

export interface ActionResult {
  ok: boolean;
  message?: string;
}

async function run(fn: () => Promise<unknown>): Promise<ActionResult> {
  try {
    await fn();
    revalidatePath('/users');
    revalidatePath('/audit');
    return { ok: true };
  } catch (error) {
    // The API's wording is already specific ("You cannot remove your own Admin role."), so it
    // is surfaced rather than replaced.
    if (error instanceof ApiError) return { ok: false, message: error.message };
    return { ok: false, message: 'Something went wrong talking to the API.' };
  }
}

export interface CreateUserInput {
  userName: string;
  email: string;
  displayName: string;
  password: string;
  roles: string[];
}

export async function createUser(input: CreateUserInput) {
  return await run(() =>
    apiFetch('/api/v1/users', { method: 'POST', body: JSON.stringify(input) }),
  );
}

export async function setRoles(id: string, roles: string[]) {
  return await run(() =>
    apiFetch(`/api/v1/users/${id}/roles`, { method: 'PUT', body: JSON.stringify({ roles }) }),
  );
}

export async function resetPassword(id: string, newPassword: string) {
  return await run(() =>
    apiFetch(`/api/v1/users/${id}/reset-password`, {
      method: 'POST',
      body: JSON.stringify({ newPassword }),
    }),
  );
}

export async function setEnabled(id: string, enabled: boolean) {
  return await run(() =>
    apiFetch(`/api/v1/users/${id}/${enabled ? 'enable' : 'disable'}`, {
      method: 'POST',
      body: JSON.stringify({}),
    }),
  );
}
