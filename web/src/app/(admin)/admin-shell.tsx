'use client';

import { useDisclosure } from '@mantine/hooks';
import {
  AppShell, AppShellHeader, AppShellMain, Burger, Button, Divider, Drawer, Group, ScrollArea,
  Stack, Text,
} from '@mantine/core';
import { IconLogout } from '@tabler/icons-react';
import { useT } from '@/i18n/client';
import { AdminNav } from './admin-nav';
import { AccountMenu } from './account-menu';
import { LanguageSwitcher } from './language-switcher';
import { LiveUpdates } from './live-updates';

/**
 * The frame around every admin page.
 *
 * The navigation lives in a drawer at every width rather than a permanent sidebar: on a phone
 * a fixed column costs a third of the screen, and on a desktop it was mostly empty. One
 * behaviour everywhere also means one thing to reason about.
 *
 * A Client Component because the drawer's open state lives here; only plain data crosses the
 * boundary from the layout.
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
    <AppShell header={{ height: 56 }} padding="md">
      <AppShellHeader>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap" gap="xs">
          <Group gap="sm" wrap="nowrap">
            <Burger opened={opened} onClick={toggle} size="sm" aria-label={t('nav.menu')} />
            <Text fw={600} truncate>{t('app.title')}</Text>
          </Group>

          {/* Stays in the header: burying a language switcher two taps deep is worse than the
              small amount of room it costs. */}
          <LanguageSwitcher />
        </Group>
      </AppShellHeader>

      <Drawer
        opened={opened}
        onClose={close}
        size={280}
        // Zero so the nav rows run edge to edge; the header gets its own inset below, because
        // otherwise the title sits flush against the side of the screen.
        padding={0}
        title={t('app.title')}
        // Scrolls its own body, so a long menu never pushes the account section out of reach.
        scrollAreaComponent={ScrollArea.Autosize}
        styles={{
          title: { fontWeight: 600 },
          header: { paddingInline: 'var(--mantine-spacing-xs)' },
        }}
      >
        <Stack gap={0} pb="md">
          <AdminNav isAdmin={isAdmin} onNavigate={close} />

          <Divider my="xs" />

          <Text size="xs" c="dimmed" tt="uppercase" fw={600} px="md" pb={4}>
            {t('nav.account')}
          </Text>

          {/* The name opens onto what you can do about the account; signing out sits beneath
              it, outside the fold, because it should never take two steps to reach. */}
          <AccountMenu displayName={displayName} roles={roles} onNavigate={close} />

          <form action="/api/auth/logout" method="post">
            {/* A real form post, so signing out also revokes the refresh token server-side. */}
            <Button
              type="submit"
              variant="subtle"
              color="red"
              justify="flex-start"
              fullWidth
              radius={0}
              leftSection={<IconLogout size={18} />}
              px="md"
            >
              {t('nav.signOut')}
            </Button>
          </form>
        </Stack>
      </Drawer>

      <AppShellMain>
        <LiveUpdates />
        {children}
      </AppShellMain>
    </AppShell>
  );
}
