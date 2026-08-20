import { redirect } from 'next/navigation';
import { Stack, Title } from '@mantine/core';
import { apiFetch, getSession, isAdmin } from '@/lib/api';
import type { components } from '@/lib/api-types';
import type { Paged } from '@/lib/categories';
import { UsersTable } from './users-table';

export type ManagedUser = components['schemas']['ManagedUserResponse'];

export default async function UsersPage() {
  const session = await getSession();
  
  if (!isAdmin(session)) {
    redirect('/categories');
  }

  const users = await apiFetch<Paged<ManagedUser>>('/api/v1/users?pageSize=100');

  return (
    <Stack>
      <div>
        <Title order={2}>User Management</Title>
      </div>

      <UsersTable users={users.items} currentUserId={session.userId ?? ''} />
    </Stack>
  );
}
