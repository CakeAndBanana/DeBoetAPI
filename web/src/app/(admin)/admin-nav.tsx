'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { NavLink } from '@mantine/core';
import { IconHistory, IconListTree, IconUsers } from '@tabler/icons-react';
import { useT } from '@/i18n/client';

/**
 * Client component on purpose: Mantine's NavLink is a client component, and React refuses to
 * let a Server Component pass it a function prop like `component={Link}`.
 */
export function AdminNav({ isAdmin, onNavigate }: { isAdmin: boolean; onNavigate?: () => void }) {
  const pathname = usePathname();
  const t = useT();

  return (
    <>
      <NavLink
        component={Link}
        onClick={onNavigate}
        href="/categories"
        label={t('nav.categories')}
        leftSection={<IconListTree size={20} />}
        active={pathname.startsWith('/categories')}
      />
      {/* Presentation only — the API refuses editors regardless of what is rendered. */}
      {isAdmin && (
        <>
          <NavLink
            component={Link}
            onClick={onNavigate}
            href="/users"
            label={t('nav.users')}
            leftSection={<IconUsers size={20} />}
            active={pathname.startsWith('/users')}
          />
          <NavLink
            component={Link}
            onClick={onNavigate}
            href="/audit"
            label={t('nav.history')}
            leftSection={<IconHistory size={20} />}
            active={pathname.startsWith('/audit')}
          />
        </>
      )}
    </>
  );
}
