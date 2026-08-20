'use client';

import { useDisclosure } from '@mantine/hooks';
import {
  AppShell, AppShellHeader, AppShellMain, AppShellNavbar, Burger, Button, Group, Text,
} from '@mantine/core';
import { useT } from '@/i18n/client';
import { AdminNav } from './admin-nav';
import { LanguageSwitcher } from './language-switcher';

/**
 * The frame around every admin page.
 *
 * A Client Component because the navbar's open state lives here: below the `sm` breakpoint it
 * collapses behind a burger instead of eating a third of a phone screen. Above it, the burger
 * is hidden and the navbar is permanent, so desktop behaviour is unchanged.
 *
 * The signed-in name is `visibleFrom="sm"` — on a narrow header the language picker and the
 * way out matter more than a label repeating who you are.
 */
export function AdminShell({
  displayName, roles, isAdmin, children,
}: {
  displayName: string | undefined;
  roles: string[] | undefined;
  isAdmin: boolean;
  children: React.ReactNode;
}) {
  const t = useT();
  const [opened, { toggle, close }] = useDisclosure(false);

  return (
    <AppShell
      header={{ height: 56 }}
      navbar={{ width: 220, breakpoint: 'sm', collapsed: { mobile: !opened } }}
      padding="md"
    >
      <AppShellHeader>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap" gap="xs">
          <Group gap="sm" wrap="nowrap">
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" aria-label={t('nav.menu')} />
            <Text fw={600} truncate>{t('app.title')}</Text>
          </Group>

          <Group gap="xs" wrap="nowrap">
            <Text size="sm" c="dimmed" visibleFrom="sm">
              {displayName ?? t('nav.signedIn')}
              {/* The roles as they actually are. Printing "editor" for anything that is not
                  admin would misreport a session whose roles have not been read yet. */}
              {roles?.length ? ` (${roles.join(', ').toLowerCase()})` : ''}
            </Text>
            <LanguageSwitcher />
            <Button
              component="a"
              href="/change-password"
              variant="subtle"
              size="compact-sm"
              visibleFrom="sm"
            >
              {t('nav.password')}
            </Button>
            {/* A real form post, so signing out also revokes the refresh token server-side. */}
            <form action="/api/auth/logout" method="post">
              <Button type="submit" variant="subtle" size="compact-sm">{t('nav.signOut')}</Button>
            </form>
          </Group>
        </Group>
      </AppShellHeader>

      <AppShellNavbar p="xs">
        {/* Closing on navigation matters only on mobile, where the navbar covers the page —
            on desktop it is permanent and `close` changes nothing. */}
        <AdminNav isAdmin={isAdmin} onNavigate={close} />
        {/* Reachable on mobile, where the header button is hidden. */}
        <Button
          component="a"
          href="/change-password"
          variant="subtle"
          size="compact-sm"
          mt="sm"
          hiddenFrom="sm"
        >
          {t('nav.password')}
        </Button>
      </AppShellNavbar>

      <AppShellMain>{children}</AppShellMain>
    </AppShell>
  );
}
