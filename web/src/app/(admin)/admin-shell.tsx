'use client';

import { useDisclosure } from '@mantine/hooks';
import {
  AppShell, AppShellHeader, AppShellMain, Burger, Button, Divider, Drawer, Group, ScrollArea,
  Stack, Text, ThemeIcon,
} from '@mantine/core';
import { IconLeaf, IconLogout } from '@tabler/icons-react';
import { useT } from '@/i18n/client';
import { AdminNav } from './admin-nav';
import { AccountMenu } from './account-menu';
import { ColorSchemeToggle } from '@/components/color-scheme-toggle';
import { LanguageSwitcher } from '@/components/language-switcher';
import { LiveUpdates } from './live-updates';

// These variables are scoped to the authenticated shell, keeping the login screen's lighter
// standalone treatment intact. Mantine components use `--mantine-border` for their standard
// outlines, so setting it here keeps cards, fields, tables, and dividers visually consistent.
const authenticatedShellStyle: React.CSSProperties & Record<string, string> = {
  backgroundColor: 'var(--panel-surface)',
  '--mantine-color-default-border': 'var(--panel-border)',
  '--mantine-border': '2px solid var(--panel-border)',
};

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
    <AppShell header={{ height: 64 }} padding="md" style={authenticatedShellStyle}>
      <AppShellHeader
        style={{
          background: 'linear-gradient(105deg, var(--mantine-color-myColor-9), var(--mantine-color-myColor-6))',
          borderBottom: '2px solid var(--panel-border)',
          boxShadow: '0 4px 14px rgba(5, 64, 10, 0.16)',
        }}
      >
        <Group h="100%" px="md" justify="space-between" wrap="nowrap" gap="xs">
          <Group gap="sm" wrap="nowrap">
            <Burger opened={opened} onClick={toggle} size="sm" color="white" aria-label={t('nav.menu')} />
            <ThemeIcon
              variant="white"
              color="myColor"
              size={36}
              radius="md"
              aria-hidden="true"
              visibleFrom="xs"
            >
              <IconLeaf size={21} stroke={2.2} />
            </ThemeIcon>
            <Stack gap={0} visibleFrom="xs">
              <Text fw={700} c="white" lh={1.15} truncate>{t('app.title')}</Text>
              <Text size="xs" c="myColor.1" fw={500}>{t('app.subtitle')}</Text>
            </Stack>
            <Text fw={700} c="white" hiddenFrom="xs" truncate>{t('app.title')}</Text>
          </Group>

          {/* Stays in the header: burying a language switcher two taps deep is worse than the
              small amount of room it costs. */}
          <Group gap={4} wrap="nowrap">
            <ColorSchemeToggle />
            <LanguageSwitcher />
          </Group>
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
