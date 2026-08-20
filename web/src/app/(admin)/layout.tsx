import {
  AppShell, AppShellHeader, AppShellMain, AppShellNavbar, Button, Group, Text,
} from '@mantine/core';
import { getSession, isAdmin } from '@/lib/api';
import { AdminNav } from './admin-nav';

/**
 * Kept as a Server Component so the signed-in name comes from the sealed session without a
 * round trip. Note the flat `AppShellHeader` imports rather than `AppShell.Header`: dot
 * notation resolves to undefined here, because a Server Component only ever holds a
 * *reference* to a client component, not the object with its sub-components attached.
 */
export default async function AdminLayout({ children }: { children: React.ReactNode }) {
  const session = await getSession();

  return (
    <AppShell header={{ height: 56 }} navbar={{ width: 220, breakpoint: 'sm' }} padding="md">
      <AppShellHeader>
        <Group h="100%" px="md" justify="space-between">
          <Text fw={600}>DeBoet Admin</Text>
          <Group gap="sm">
            <Text size="sm" c="dimmed">
              {session.displayName ?? 'signed in'}
              {isAdmin(session) ? ' (admin)' : ' (editor)'}
            </Text>
            {/* component="a", not component={Link}: a Server Component may not pass a
                function prop to a client component like Button. */}
            <Button component="a" href="/change-password" variant="subtle" size="compact-sm">
              Password
            </Button>
            {/* A real form post, so signing out also revokes the refresh token server-side. */}
            <form action="/api/auth/logout" method="post">
              <Button type="submit" variant="subtle" size="compact-sm">Sign out</Button>
            </form>
          </Group>
        </Group>
      </AppShellHeader>

      <AppShellNavbar p="xs">
        <AdminNav isAdmin={isAdmin(session)} />
      </AppShellNavbar>

      <AppShellMain>{children}</AppShellMain>
    </AppShell>
  );
}
