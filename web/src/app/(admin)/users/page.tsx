import { redirect } from 'next/navigation';
import { Stack, Text, Title } from '@mantine/core';
import { apiFetch, getSession, isAdmin } from '@/lib/api';
import type { components } from '@/lib/api-types';
import type { Paged } from '@/lib/categories';
import { UsersTable } from './users-table';

export type ManagedUser = components['schemas']['ManagedUserResponse'];

export default async function UsersPage() {
  const session = await getSession();

  // Editors have no business here. The API refuses them anyway; this avoids rendering a page
  // whose every request would 403.
  if (!isAdmin(session)) {
    redirect('/categories');
  }

  const users = await apiFetch<Paged<ManagedUser>>('/api/v1/users?pageSize=100');

  return (
    <Stack>
      <div>
        <Title order={2}>Users</Title>
        <Text c="dimmed" size="sm">
          There is no self-service password recovery — resetting a password here is how someone
          who is locked out gets back in.
        </Text>
      </div>

      <UsersTable users={users.items} currentUserId={session.userId ?? ''} />
    </Stack>
  );
}
